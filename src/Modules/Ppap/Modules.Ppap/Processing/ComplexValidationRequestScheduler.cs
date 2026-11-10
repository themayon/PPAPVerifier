using System.Security.Cryptography;
using System.Text;
using DES.Framework.Eventing.Abstractions;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.Services;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Rules.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DES.Modules.Ppap.Processing;

public interface IComplexValidationRequestScheduler
{
    Task ScheduleReadyAsync(
        PpapSubmission submission,
        string correlationId,
        DateTime occurredOnUtc,
        CancellationToken cancellationToken = default);
}

internal sealed class ComplexValidationRequestScheduler(
    PpapDbContext dbContext,
    IComplexValidationPlanReader planReader,
    ICanonicalDocumentReader canonicalDocumentReader,
    IExtractionProvenanceReader provenanceReader,
    IOutboxWriter outbox,
    IOptions<ComplexValidationOptions> options) : IComplexValidationRequestScheduler
{
    public async Task ScheduleReadyAsync(
        PpapSubmission submission,
        string correlationId,
        DateTime occurredOnUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submission);
        if (!options.Value.Enabled || submission.RuleTemplateVersionId is not { } templateVersionId) return;

        IReadOnlyList<ComplexValidationPlanItem> plan = await planReader
            .ReadAsync(templateVersionId, cancellationToken)
            .ConfigureAwait(false);
        if (plan.Count == 0) return;

        Guid[] documentIds = submission.Documents.Select(document => document.Id).ToArray();
        PpapDocumentRevision[] currentRevisions = await dbContext.PpapDocumentRevisions
            .Where(revision => documentIds.Contains(revision.SubmissionDocumentId) && revision.IsCurrent)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, PpapSubmissionDocument> documentsById = submission.Documents.ToDictionary(document => document.Id);
        Dictionary<string, PpapDocumentRevision> readyByCode = currentRevisions
            .Where(revision => revision.ProcessingStatus == DocumentProcessingStatus.Ready
                && revision.ExtractionAttemptId.HasValue
                && revision.CanonicalDocumentId.HasValue)
            .ToDictionary(
                revision => documentsById[revision.SubmissionDocumentId].DocumentCode,
                StringComparer.Ordinal);
        ComplexValidationPlanItem[] readyPlan = plan
            .Where(item => item.RequiredDocumentCodes.All(readyByCode.ContainsKey))
            .ToArray();
        if (readyPlan.Length == 0) return;

        PpapDocumentRevision[] requiredRevisions = readyPlan
            .SelectMany(item => item.RequiredDocumentCodes.Select(code => readyByCode[code]))
            .DistinctBy(revision => revision.Id)
            .ToArray();
        Guid[] canonicalDocumentIds = requiredRevisions.Select(revision => revision.CanonicalDocumentId!.Value).ToArray();
        Guid[] extractionAttemptIds = requiredRevisions.Select(revision => revision.ExtractionAttemptId!.Value).ToArray();
        IReadOnlyList<CanonicalDocumentDto> canonicalDocuments = await canonicalDocumentReader
            .ReadAsync(submission.OwnerUserId, canonicalDocumentIds, cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<ExtractionProvenanceDto> provenance = await provenanceReader
            .ReadAsync(submission.OwnerUserId, extractionAttemptIds, cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, CanonicalDocumentDto> canonicalById = canonicalDocuments.ToDictionary(document => document.Id);
        Dictionary<Guid, ExtractionProvenanceDto> provenanceByAttemptId = provenance.ToDictionary(item => item.ExtractionAttemptId);

        foreach (ComplexValidationPlanItem item in readyPlan)
        {
            PpapDocumentRevision[] inputs = item.RequiredDocumentCodes
                .Select(code => readyByCode[code])
                .OrderBy(revision => documentsById[revision.SubmissionDocumentId].DocumentCode, StringComparer.Ordinal)
                .ToArray();
            string inputFingerprint = ComputeFingerprint(item, inputs, documentsById, provenanceByAttemptId);
            bool duplicate = dbContext.PpapComplexValidationRequests.Local.Any(request =>
                    request.SubmissionId == submission.Id
                    && request.RuleVersionId == item.RuleVersionId
                    && request.InputFingerprint == inputFingerprint)
                || await dbContext.PpapComplexValidationRequests.AnyAsync(request =>
                    request.SubmissionId == submission.Id
                    && request.RuleVersionId == item.RuleVersionId
                    && request.InputFingerprint == inputFingerprint,
                    cancellationToken).ConfigureAwait(false);
            if (duplicate) continue;

            Guid requestEventId = Guid.CreateVersion7();
            Guid validationRunId = Guid.CreateVersion7();
            PpapComplexValidationRequest request = PpapComplexValidationRequest.Create(
                submission.TenantId,
                submission.Id,
                item.RuleVersionId,
                requestEventId,
                validationRunId,
                item.ExecutorKey,
                inputFingerprint,
                occurredOnUtc);
            dbContext.PpapComplexValidationRequests.Add(request);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            ComplexValidationDocumentInput[] eventDocuments = inputs.Select(revision =>
            {
                PpapSubmissionDocument document = documentsById[revision.SubmissionDocumentId];
                ExtractionProvenanceDto source = provenanceByAttemptId[revision.ExtractionAttemptId!.Value];
                CanonicalDocumentDto canonical = canonicalById[revision.CanonicalDocumentId!.Value];
                return new ComplexValidationDocumentInput(
                    document.Id,
                    revision.Id,
                    document.DocumentCode,
                    source.SourceSha256,
                    canonical.SchemaVersion,
                    canonical.CanonicalJson);
            }).ToArray();
            await outbox.AddAsync(new ComplexValidationRequestedIntegrationEvent(
                requestEventId,
                occurredOnUtc,
                submission.TenantId,
                string.IsNullOrWhiteSpace(correlationId) ? requestEventId.ToString() : correlationId,
                "Ppap",
                submission.Id,
                validationRunId,
                item.RuleVersionId,
                item.StableRuleCode,
                item.RuleName,
                item.ExecutorKey,
                item.ParametersJson,
                inputFingerprint,
                submission.OwnerUserId,
                eventDocuments), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ComputeFingerprint(
        ComplexValidationPlanItem plan,
        IReadOnlyList<PpapDocumentRevision> revisions,
        IReadOnlyDictionary<Guid, PpapSubmissionDocument> documentsById,
        IReadOnlyDictionary<Guid, ExtractionProvenanceDto> provenanceByAttemptId)
    {
        var value = new StringBuilder()
            .Append(plan.RuleVersionId.ToString("N"))
            .Append('|').Append(plan.ExecutorKey)
            .Append('|').Append(plan.ParametersJson);
        foreach (PpapDocumentRevision revision in revisions)
        {
            ExtractionProvenanceDto source = provenanceByAttemptId[revision.ExtractionAttemptId!.Value];
            _ = value.Append('|')
                .Append(documentsById[revision.SubmissionDocumentId].DocumentCode)
                .Append(':').Append(revision.Id.ToString("N"))
                .Append(':').Append(source.SourceSha256);
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString())));
    }
}
