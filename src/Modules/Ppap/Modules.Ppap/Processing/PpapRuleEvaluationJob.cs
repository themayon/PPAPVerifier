using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Framework.Jobs.Services;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.Services;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Rules.Contracts;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DES.Modules.Ppap.Processing;

public sealed class PpapRuleEvaluationJob(
    IMultiTenantStore<AppTenantInfo> tenantStore,
    IMultiTenantContextSetter tenantContextSetter,
    IServiceScopeFactory scopeFactory)
{
    private const int CrossValidationProgress = 60;

    public async Task RunAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Job ID is required.", nameof(jobId));
        }

        AppTenantInfo tenant = await tenantStore.GetAsync(tenantId).ConfigureAwait(false)
            ?? throw new NotFoundException($"Tenant {tenantId} not found for PPAP rule evaluation job {jobId}.");
        tenantContextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(tenant);

        using IServiceScope scope = scopeFactory.CreateScope();
        PpapDbContext dbContext = scope.ServiceProvider.GetRequiredService<PpapDbContext>();
        ICanonicalDocumentReader canonicalDocumentReader = scope.ServiceProvider.GetRequiredService<ICanonicalDocumentReader>();
        IRuleEvaluationService ruleEvaluationService = scope.ServiceProvider.GetRequiredService<IRuleEvaluationService>();
        IJobService jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

        PpapJob job = await dbContext.PpapJobs
            .FirstOrDefaultAsync(x => x.Id == jobId && x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP job {jobId} not found.");

        if (job.RuleEvaluationRunId is not null && job.RuleEvaluationCompletedAtUtc is not null)
        {
            if (job.Status == PpapJobStatus.Processing && job.CurrentStage == PpapJobStage.CrossValidation)
            {
                await QueueReportGenerationAsync(dbContext, jobService, job, tenantId, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (job.Status == PpapJobStatus.Completed
                || (job.Status == PpapJobStatus.Processing && job.CurrentStage >= PpapJobStage.ReportGeneration))
            {
                return;
            }
        }
        if (job.Status != PpapJobStatus.Processing || job.CurrentStage != PpapJobStage.DocumentClassification)
        {
            throw Conflict("The PPAP job is not waiting for rule evaluation.");
        }
        if (job.RuleEvaluationQueuedAtUtc is null)
        {
            throw Conflict("The PPAP job rule evaluation was not queued.");
        }

        PpapSubmission submission = await dbContext.PpapSubmissions
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(
                x => x.Id == job.SubmissionId
                    && x.TenantId == tenantId
                    && x.OwnerUserId == job.OwnerUserId
                    && x.Version == job.SubmissionVersion,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP submission {job.SubmissionId} was not found for rule evaluation job {jobId}.");

        PpapSubmissionDocument[] documents = GetSuccessfulDocuments(submission);
        _ = job.MarkRuleEvaluationStarted();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Guid[] canonicalDocumentIds = documents
            .Select(x => x.CanonicalDocumentId!.Value)
            .ToArray();
        IReadOnlyList<CanonicalDocumentDto> canonicalDocuments = await canonicalDocumentReader
            .ReadAsync(job.OwnerUserId, canonicalDocumentIds, cancellationToken)
            .ConfigureAwait(false);
        RuleEvaluationRequest request = BuildRequest(job, submission, documents, canonicalDocuments);
        Guid runId = await ruleEvaluationService.EvaluateAsync(request, cancellationToken).ConfigureAwait(false);

        dbContext.ChangeTracker.Clear();
        job = await dbContext.PpapJobs
            .FirstOrDefaultAsync(
                x => x.Id == jobId
                    && x.TenantId == tenantId
                    && x.SubmissionId == submission.Id
                    && x.SubmissionVersion == submission.Version
                    && x.OwnerUserId == submission.OwnerUserId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP job {jobId} was not found after rule evaluation.");

        if (job.RuleEvaluationCompletedAtUtc is not null)
        {
            if (job.RuleEvaluationRunId != runId)
            {
                throw Conflict("The PPAP job has a conflicting rule evaluation run.");
            }
            await QueueReportGenerationAsync(dbContext, jobService, job, tenantId, cancellationToken).ConfigureAwait(false);
            return;
        }

        _ = job.CompleteRuleEvaluation(runId);
        job.AdvanceStage(PpapJobStage.CrossValidation, CrossValidationProgress);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await QueueReportGenerationAsync(dbContext, jobService, job, tenantId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task QueueReportGenerationAsync(
        PpapDbContext dbContext,
        IJobService jobService,
        PpapJob job,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (job.ReportGenerationQueuedAtUtc is not null) return;

        jobService.Enqueue<PpapReportGenerationJob>(
            reportJob => reportJob.RunAsync(tenantId, job.Id, CancellationToken.None));
        _ = job.MarkReportGenerationQueued();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static RuleEvaluationRequest BuildRequest(
        PpapJob job,
        PpapSubmission submission,
        IReadOnlyCollection<PpapSubmissionDocument> documents,
        IReadOnlyCollection<CanonicalDocumentDto> canonicalDocuments)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(canonicalDocuments);

        if (submission.RuleTemplateVersionId is not Guid templateVersionId || templateVersionId == Guid.Empty)
        {
            throw Conflict("The PPAP submission does not have a snapshotted rule template version.");
        }
        if (documents.Count == 0 || documents.Count != canonicalDocuments.Count)
        {
            throw Conflict("Canonical documents do not exactly correlate to the PPAP submission documents.");
        }

        Dictionary<Guid, CanonicalDocumentDto> canonicalById;
        try
        {
            canonicalById = canonicalDocuments.ToDictionary(x => x.Id);
        }
        catch (ArgumentException exception)
        {
            throw Conflict("Canonical document identifiers must be unique.", exception);
        }

        var documentJson = new Dictionary<string, string>(documents.Count, StringComparer.Ordinal);
        string? schemaVersion = null;
        foreach (PpapSubmissionDocument document in documents.OrderBy(x => x.DocumentCode, StringComparer.Ordinal))
        {
            if (document.ExtractionTerminalStatus != DocumentExtractionTerminalStatus.Succeeded
                || document.ExtractionAttemptId is not Guid extractionAttemptId
                || document.CanonicalDocumentId is not Guid canonicalDocumentId
                || !canonicalById.Remove(canonicalDocumentId, out CanonicalDocumentDto? canonicalDocument)
                || canonicalDocument.ExtractionAttemptId != extractionAttemptId)
            {
                throw Conflict("A canonical document does not correlate to its successful PPAP extraction.");
            }
            if (string.IsNullOrWhiteSpace(canonicalDocument.SchemaVersion))
            {
                throw Conflict("Canonical document schema version is required.");
            }

            schemaVersion ??= canonicalDocument.SchemaVersion;
            if (!string.Equals(schemaVersion, canonicalDocument.SchemaVersion, StringComparison.Ordinal))
            {
                // Mixed schemas cannot be interpreted safely as one Rules input contract, so reject deterministically.
                throw Conflict("All canonical PPAP documents must use the same schema version.");
            }

            documentJson.Add(document.DocumentCode, canonicalDocument.CanonicalJson);
        }
        if (canonicalById.Count != 0)
        {
            throw Conflict("Canonical documents do not exactly correlate to the PPAP submission documents.");
        }

        return new RuleEvaluationRequest(
            templateVersionId,
            submission.Id,
            job.OwnerUserId,
            schemaVersion!,
            documentJson);
    }

    private static PpapSubmissionDocument[] GetSuccessfulDocuments(PpapSubmission submission)
    {
        PpapSubmissionDocument[] documents = submission.Documents
            .Where(x => x.Status == SubmissionDocumentStatus.Available)
            .OrderBy(x => x.DocumentCode, StringComparer.Ordinal)
            .ToArray();
        if (documents.Length == 0
            || documents.Any(x => x.ExtractionRequestPublishedAtUtc is null
                || x.ExtractionTerminalStatus != DocumentExtractionTerminalStatus.Succeeded
                || x.ExtractionAttemptId is null
                || x.CanonicalDocumentId is null))
        {
            throw Conflict("Rule evaluation requires successful extraction of every available PPAP document.");
        }

        return documents;
    }

    private static CustomException Conflict(string message, Exception? innerException = null) =>
        innerException is null
            ? new CustomException(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict)
            : new CustomException(message, innerException, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
