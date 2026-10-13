using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.Services;
using DES.Framework.Eventing.Abstractions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Rules.Contracts;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DES.Modules.Ppap.Processing;

public sealed class PpapReportGenerationJob(
    IMultiTenantStore<AppTenantInfo> tenantStore,
    IMultiTenantContextSetter tenantContextSetter,
    IServiceScopeFactory scopeFactory)
{
    private const int ReportGenerationProgress = 80;
    private const int NotificationDeliveryProgress = 95;

    public async Task RunAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (jobId == Guid.Empty) throw new ArgumentException("Job ID is required.", nameof(jobId));

        AppTenantInfo tenant = await tenantStore.GetAsync(tenantId).ConfigureAwait(false)
            ?? throw new NotFoundException($"Tenant {tenantId} not found for PPAP report generation job {jobId}.");
        tenantContextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(tenant);

        using IServiceScope scope = scopeFactory.CreateScope();
        PpapDbContext dbContext = scope.ServiceProvider.GetRequiredService<PpapDbContext>();
        IRuleEvaluationResultReader resultReader = scope.ServiceProvider.GetRequiredService<IRuleEvaluationResultReader>();
        IExtractionProvenanceReader provenanceReader = scope.ServiceProvider.GetRequiredService<IExtractionProvenanceReader>();
        IOutboxWriter outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        PpapJob job = await dbContext.PpapJobs
            .FirstOrDefaultAsync(x => x.Id == jobId && x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP job {jobId} not found.");
        if (job.Status == PpapJobStatus.Completed)
        {
            return;
        }
        if (job.Status != PpapJobStatus.Processing
            || job.CurrentStage is not (PpapJobStage.CrossValidation or PpapJobStage.ReportGeneration)
            || job.RuleEvaluationRunId is not Guid evaluationRunId
            || job.RuleEvaluationCompletedAtUtc is null
            || job.ReportGenerationQueuedAtUtc is null)
        {
            throw Conflict("The PPAP job is not ready for report generation.");
        }

        _ = job.MarkReportGenerationStarted();
        if (job.CurrentStage == PpapJobStage.CrossValidation)
        {
            job.AdvanceStage(PpapJobStage.ReportGeneration, ReportGenerationProgress);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        PpapSubmission submission = await dbContext.PpapSubmissions
            .AsNoTracking()
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == job.SubmissionId
                && x.TenantId == tenantId
                && x.OwnerUserId == job.OwnerUserId
                && x.Version == job.SubmissionVersion, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP submission {job.SubmissionId} was not found for report generation job {jobId}.");
        RuleEvaluationRunDetails evaluation = await resultReader
            .ReadAsync(evaluationRunId, job.OwnerUserId, submission.Id, cancellationToken)
            .ConfigureAwait(false);
        if (evaluation.TemplateVersionId != submission.RuleTemplateVersionId)
        {
            throw Conflict("The rule evaluation template does not match the PPAP submission snapshot.");
        }

        PpapSubmissionDocument[] successfulDocuments = submission.Documents
            .Where(x => x.Status == SubmissionDocumentStatus.Available
                && x.ExtractionTerminalStatus == DocumentExtractionTerminalStatus.Succeeded
                && x.ExtractionAttemptId is not null)
            .OrderBy(x => x.DocumentCode, StringComparer.Ordinal)
            .ThenBy(x => x.Id)
            .ToArray();
        if (successfulDocuments.Length != submission.Documents.Count(x => x.Status == SubmissionDocumentStatus.Available))
        {
            throw Conflict("Every available PPAP document must have a successful extraction before report generation.");
        }
        Guid[] extractionAttemptIds = successfulDocuments
            .Select(x => x.ExtractionAttemptId!.Value)
            .ToArray();
        IReadOnlyList<ExtractionProvenanceDto> provenance = await provenanceReader
            .ReadAsync(job.OwnerUserId, extractionAttemptIds, cancellationToken)
            .ConfigureAwait(false);
        string manifestJson = BuildReproducibilityManifest(job, submission, evaluation, provenance);

        RuleEvaluationResultDetails[] findingResults = evaluation.Results
            .Where(x => x.Status != RuleEvaluationStatus.Passed)
            .ToArray();
        Dictionary<string, PpapSubmissionDocument> documentsByCode = submission.Documents
            .ToDictionary(x => x.DocumentCode, StringComparer.OrdinalIgnoreCase);

        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        HashSet<Guid> existingResultIds = (await dbContext.PpapFindings
            .Where(x => x.TenantId == tenantId && x.JobId == job.Id)
            .Select(x => x.RuleEvaluationResultId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).ToHashSet();
        foreach (RuleEvaluationResultDetails result in findingResults.Where(x => !existingResultIds.Contains(x.Id)))
        {
            PpapSubmissionDocument? document = null;
            if (result.DocumentCode is not null
                && (!documentsByCode.TryGetValue(result.DocumentCode, out document)
                    || document.ExtractionTerminalStatus != DocumentExtractionTerminalStatus.Succeeded
                    || document.CanonicalDocumentId is null
                    || document.ExtractionAttemptId is null))
            {
                throw Conflict($"Finding {result.Id} does not correlate to a successfully extracted PPAP document.");
            }

            dbContext.PpapFindings.Add(PpapFinding.Create(
                tenantId,
                submission.Id,
                job.Id,
                evaluation.Id,
                result.Id,
                evaluation.TemplateVersionId,
                result.RuleDefinitionId,
                result.RuleVersionId,
                job.OwnerUserId,
                result.StableRuleCode,
                result.RuleName,
                result.DocumentCode,
                MapOutcome(result.Status),
                Truncate(result.Message, PpapFinding.MessageMaxLength),
                Truncate(result.Reason, PpapFinding.ReasonMaxLength),
                result.TraceJson,
                result.EvaluatedAtUtc,
                document?.Id,
                document?.CanonicalDocumentId,
                document?.ExtractionAttemptId));
        }

        PpapReport? report = await dbContext.PpapReports
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.JobId == job.Id, cancellationToken)
            .ConfigureAwait(false);
        if (report is null)
        {
            int failedCount = findingResults.Count(x => x.Status == RuleEvaluationStatus.Failed);
            int notEvaluatedCount = findingResults.Count(x => x.Status == RuleEvaluationStatus.NotEvaluated);
            int errorCount = findingResults.Count(x => x.Status == RuleEvaluationStatus.Error);
            report = PpapReport.Create(
                tenantId,
                submission.Id,
                job.Id,
                submission.Version,
                submission.OwnerUserId,
                submission.Documents.Count(x => x.Status == SubmissionDocumentStatus.Available),
                submission.Documents.Count(x => x.Status == SubmissionDocumentStatus.NotApplicable),
                submission.Documents.Count(x => x.Status == SubmissionDocumentStatus.Quarantined),
                findingResults.Length,
                failedCount,
                notEvaluatedCount,
                errorCount,
                manifestJson);
            dbContext.PpapReports.Add(report);
        }

        _ = job.MarkReportGenerationCompleted(report.Id);
        job.AdvanceStage(PpapJobStage.NotificationDelivery, NotificationDeliveryProgress);
        job.Complete();
        Guid eventId = job.CompletionEventId ?? Guid.CreateVersion7();
        if (job.MarkCompletionEventPublished(eventId))
        {
            await outbox.AddAsync(new PpapProcessingCompletedIntegrationEvent(
                eventId,
                DateTime.UtcNow,
                tenantId,
                Activity.Current?.Id ?? eventId.ToString(),
                "Ppap",
                job.Id,
                submission.Id,
                report.Id,
                submission.Version,
                submission.OwnerUserId,
                submission.PartName,
                submission.PartNumber), cancellationToken).ConfigureAwait(false);
        }
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static string BuildReproducibilityManifest(
        PpapJob job,
        PpapSubmission submission,
        RuleEvaluationRunDetails evaluation,
        IReadOnlyCollection<ExtractionProvenanceDto> provenance)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(evaluation);
        ArgumentNullException.ThrowIfNull(provenance);

        if (job.Id == Guid.Empty
            || job.SubmissionId != submission.Id
            || job.SubmissionVersion != submission.Version
            || job.OwnerUserId != submission.OwnerUserId
            || evaluation.Id != job.RuleEvaluationRunId
            || evaluation.SubjectId != submission.Id
            || evaluation.OwnerUserId != submission.OwnerUserId
            || submission.RuleTemplateVersionId is not Guid templateVersionId
            || templateVersionId == Guid.Empty
            || evaluation.TemplateVersionId != templateVersionId)
        {
            throw Conflict("The report inputs do not correlate to the PPAP job, submission, and evaluation snapshots.");
        }
        if (string.IsNullOrWhiteSpace(evaluation.InputSchemaVersion)
            || evaluation.Version <= 0
            || evaluation.CreatedAtUtc.Kind != DateTimeKind.Utc
            || evaluation.CompletedAtUtc is not DateTime completedAtUtc
            || completedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw Conflict("The rule evaluation run metadata is incomplete or invalid.");
        }

        Dictionary<Guid, ExtractionProvenanceDto> provenanceByAttemptId;
        try
        {
            provenanceByAttemptId = provenance.ToDictionary(x => x.ExtractionAttemptId);
        }
        catch (ArgumentException exception)
        {
            throw Conflict("Extraction provenance attempt identifiers must be unique.", exception);
        }

        var documentCodes = new HashSet<string>(StringComparer.Ordinal);
        PpapManifestDocumentDto[] documents = submission.Documents
            .OrderBy(x => x.DocumentCode, StringComparer.Ordinal)
            .ThenBy(x => x.Id)
            .Select(document =>
            {
                if (!documentCodes.Add(document.DocumentCode))
                {
                    throw Conflict("PPAP manifest document codes must be unique.");
                }

                PpapManifestExtractionProvenanceDto? extraction = null;
                if (document.Status == SubmissionDocumentStatus.Available)
                {
                    if (document.FileAssetId is not Guid sourceFileId
                        || document.ExtractionTerminalStatus != DocumentExtractionTerminalStatus.Succeeded
                        || document.ExtractionAttemptId is not Guid attemptId
                        || document.CanonicalDocumentId is not Guid canonicalDocumentId
                        || document.ExtractionCompletedAtUtc is null
                        || !provenanceByAttemptId.Remove(attemptId, out ExtractionProvenanceDto? item)
                        || item.SubmissionDocumentId != document.Id
                        || item.SourceFileAssetId != sourceFileId
                        || !string.Equals(item.DocumentCode, document.DocumentCode, StringComparison.Ordinal)
                        || !string.Equals(item.Status, nameof(DocumentExtractionTerminalStatus.Succeeded), StringComparison.Ordinal)
                        || item.CanonicalDocumentId != canonicalDocumentId
                        || item.RawResponseArtifactId != document.RawResponseFileAssetId
                        || item.GeneratedExcelArtifactId != document.GeneratedExcelFileAssetId)
                    {
                        throw Conflict("Extraction provenance does not correlate to the persisted PPAP document snapshot.");
                    }
                    if (string.IsNullOrWhiteSpace(item.SourceSha256)
                        || string.IsNullOrWhiteSpace(item.Provider)
                        || string.IsNullOrWhiteSpace(item.Model)
                        || string.IsNullOrWhiteSpace(item.ExtractorVersion)
                        || string.IsNullOrWhiteSpace(item.CanonicalSchemaVersion)
                        || item.CreatedAtUtc.Kind != DateTimeKind.Utc
                        || item.CompletedAtUtc.Kind != DateTimeKind.Utc)
                    {
                        throw Conflict("Extraction provenance metadata is incomplete or invalid.");
                    }

                    extraction = new PpapManifestExtractionProvenanceDto(
                        item.SourceSha256,
                        item.ExtractionAttemptId,
                        item.Provider,
                        item.Model,
                        item.ExtractorVersion,
                        item.CanonicalDocumentId,
                        item.CanonicalSchemaVersion,
                        item.RawResponseArtifactId,
                        item.GeneratedExcelArtifactId,
                        item.CreatedAtUtc,
                        item.CompletedAtUtc);
                }
                else if (document.ExtractionTerminalStatus == DocumentExtractionTerminalStatus.Succeeded)
                {
                    throw Conflict("Only available PPAP documents may have successful extraction provenance.");
                }

                return new PpapManifestDocumentDto(
                    document.Id,
                    document.DocumentCode,
                    document.Disposition.ToString(),
                    document.Status.ToString(),
                    document.FileAssetId,
                    document.Status == SubmissionDocumentStatus.NotApplicable ? document.NotApplicableReason : null,
                    extraction);
            })
            .ToArray();
        if (provenanceByAttemptId.Count != 0)
        {
            throw Conflict("Extraction provenance does not exactly correlate to the available PPAP documents.");
        }

        var resultIds = new HashSet<Guid>();
        PpapManifestEvaluationResultDto[] results = evaluation.Results
            .OrderBy(x => x.StableRuleCode, StringComparer.Ordinal)
            .ThenBy(x => x.DocumentCode, StringComparer.Ordinal)
            .ThenBy(x => x.Id)
            .Select(result =>
            {
                if (!resultIds.Add(result.Id)
                    || result.Id == Guid.Empty
                    || result.RuleDefinitionId == Guid.Empty
                    || result.RuleVersionId == Guid.Empty
                    || result.TemplateVersionId != templateVersionId
                    || string.IsNullOrWhiteSpace(result.StableRuleCode)
                    || result.EvaluatedAtUtc.Kind != DateTimeKind.Utc
                    || (result.DocumentCode is not null
                        && (!documentCodes.Contains(result.DocumentCode)
                            || !documents.Any(x => string.Equals(x.DocumentCode, result.DocumentCode, StringComparison.Ordinal)
                                && x.Extraction is not null))))
                {
                    throw Conflict("A rule evaluation result does not correlate to the report snapshot.");
                }

                return new PpapManifestEvaluationResultDto(
                    result.Id,
                    result.RuleDefinitionId,
                    result.RuleVersionId,
                    result.StableRuleCode,
                    result.DocumentCode,
                    result.Status.ToString(),
                    result.EvaluatedAtUtc);
            })
            .ToArray();

        var manifest = new PpapReproducibilityManifestDto(
            PpapReport.ReproducibilityManifestSchemaVersion,
            job.Id,
            submission.Id,
            submission.Version,
            templateVersionId,
            new PpapManifestEvaluationDto(
                evaluation.Id,
                evaluation.Version,
                evaluation.InputSchemaVersion,
                evaluation.Status.ToString(),
                evaluation.CreatedAtUtc,
                evaluation.CompletedAtUtc,
                results),
            documents);
        string manifestJson = JsonSerializer.Serialize(manifest, ManifestJsonOptions);
        if (Encoding.UTF8.GetByteCount(manifestJson) > PpapReport.ReproducibilityManifestMaxBytes)
        {
            throw Conflict("The PPAP reproducibility manifest exceeds 256 KiB.");
        }
        return manifestJson;
    }

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(JsonSerializerDefaults.Web);

    private static PpapFindingOutcome MapOutcome(RuleEvaluationStatus status) => status switch
    {
        RuleEvaluationStatus.Failed => PpapFindingOutcome.Failed,
        RuleEvaluationStatus.NotEvaluated => PpapFindingOutcome.NotEvaluated,
        RuleEvaluationStatus.Error => PpapFindingOutcome.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Passed evaluations do not create findings.")
    };

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

    private static CustomException Conflict(string message, Exception? innerException = null) =>
        innerException is null
            ? new CustomException(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict)
            : new CustomException(message, innerException, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
