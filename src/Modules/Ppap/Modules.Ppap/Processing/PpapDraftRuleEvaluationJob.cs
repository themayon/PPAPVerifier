using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.Services;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Rules.Contracts;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DES.Modules.Ppap.Processing;

public sealed partial class PpapDraftRuleEvaluationJob(
    IMultiTenantStore<AppTenantInfo> tenantStore,
    IMultiTenantContextSetter tenantContextSetter,
    IServiceScopeFactory scopeFactory,
    ILogger<PpapDraftRuleEvaluationJob> logger)
{
    public async Task RunAsync(string tenantId, Guid submissionId, Guid revisionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (submissionId == Guid.Empty) throw new ArgumentException("Submission ID is required.", nameof(submissionId));
        if (revisionId == Guid.Empty) throw new ArgumentException("Revision ID is required.", nameof(revisionId));

        AppTenantInfo tenant = await tenantStore.GetAsync(tenantId).ConfigureAwait(false)
            ?? throw new NotFoundException($"Tenant {tenantId} not found for PPAP draft rule evaluation.");
        tenantContextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(tenant);

        using IServiceScope scope = scopeFactory.CreateScope();
        PpapDbContext dbContext = scope.ServiceProvider.GetRequiredService<PpapDbContext>();
        ICanonicalDocumentReader canonicalDocumentReader = scope.ServiceProvider.GetRequiredService<ICanonicalDocumentReader>();
        IRuleEvaluationService ruleEvaluationService = scope.ServiceProvider.GetRequiredService<IRuleEvaluationService>();

        PpapSubmission? submission = await dbContext.PpapSubmissions
            .AsNoTracking()
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == submissionId && x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (submission is null
            || submission.Status != SubmissionStatus.Draft
            || submission.RuleTemplateVersionId is not Guid templateVersionId)
        {
            LogSkipped(logger, submissionId, revisionId, "submission is not a draft with a template snapshot");
            return;
        }

        Dictionary<Guid, PpapSubmissionDocument> documentsById = submission.Documents.ToDictionary(x => x.Id);
        Guid[] documentIds = documentsById.Keys.ToArray();
        PpapDocumentRevision[] readyRevisions = await dbContext.PpapDocumentRevisions
            .AsNoTracking()
            .Where(x => documentIds.Contains(x.SubmissionDocumentId)
                && x.IsCurrent
                && x.ProcessingStatus == DocumentProcessingStatus.Ready
                && x.ExtractionAttemptId != null
                && x.CanonicalDocumentId != null)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        PpapDocumentRevision? target = readyRevisions.FirstOrDefault(x => x.Id == revisionId);
        if (target is null)
        {
            LogSkipped(logger, submissionId, revisionId, "revision is no longer the current ready revision");
            return;
        }

        IReadOnlyList<CanonicalDocumentDto> canonicalDocuments = await canonicalDocumentReader
            .ReadAsync(submission.OwnerUserId, readyRevisions.Select(x => x.CanonicalDocumentId!.Value).ToArray(), cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, CanonicalDocumentDto> canonicalById = canonicalDocuments.ToDictionary(x => x.Id);
        if (!canonicalById.TryGetValue(target.CanonicalDocumentId!.Value, out CanonicalDocumentDto? targetCanonical)
            || string.IsNullOrWhiteSpace(targetCanonical.SchemaVersion))
        {
            LogSkipped(logger, submissionId, revisionId, "canonical document is unavailable");
            return;
        }

        string targetCode = documentsById[target.SubmissionDocumentId].DocumentCode;
        var readyByCode = new Dictionary<string, PpapDocumentRevision>(StringComparer.Ordinal);
        var documentJson = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PpapDocumentRevision revision in readyRevisions)
        {
            if (!canonicalById.TryGetValue(revision.CanonicalDocumentId!.Value, out CanonicalDocumentDto? canonical)
                || canonical.ExtractionAttemptId != revision.ExtractionAttemptId
                || !string.Equals(canonical.SchemaVersion, targetCanonical.SchemaVersion, StringComparison.Ordinal))
            {
                continue;
            }

            string code = documentsById[revision.SubmissionDocumentId].DocumentCode;
            readyByCode[code] = revision;
            documentJson[code] = canonical.CanonicalJson;
        }
        if (!readyByCode.ContainsKey(targetCode))
        {
            LogSkipped(logger, submissionId, revisionId, "canonical document does not correlate to the extraction attempt");
            return;
        }

        IReadOnlyList<RuleEvaluationPreviewResult> results = await ruleEvaluationService.PreviewAsync(
                new RuleEvaluationRequest(templateVersionId, submission.Id, submission.OwnerUserId, targetCanonical.SchemaVersion, documentJson),
                targetCode,
                cancellationToken)
            .ConfigureAwait(false);

        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        bool stillCurrent = await dbContext.PpapSubmissions
            .AnyAsync(x => x.Id == submissionId && x.TenantId == tenantId && x.Status == SubmissionStatus.Draft, cancellationToken)
            .ConfigureAwait(false)
            && await dbContext.PpapDocumentRevisions
            .AnyAsync(x => x.Id == revisionId && x.IsCurrent && x.ProcessingStatus == DocumentProcessingStatus.Ready, cancellationToken)
            .ConfigureAwait(false);
        if (!stillCurrent)
        {
            LogSkipped(logger, submissionId, revisionId, "revision changed during evaluation");
            return;
        }

        Guid validationRunId = Guid.CreateVersion7();
        DateTime utcNow = DateTime.UtcNow;
        int created = 0;
        foreach (RuleEvaluationPreviewResult result in results)
        {
            string scopeCode = result.DocumentCode ?? targetCode;
            if (!readyByCode.TryGetValue(scopeCode, out PpapDocumentRevision? scopeRevision)) continue;

            Guid scopeDocumentId = scopeRevision.SubmissionDocumentId;
            bool isFinding = result.Status is RuleEvaluationStatus.Failed or RuleEvaluationStatus.Error;
            string fingerprint = ComputeFingerprint(submission.Id, result.RuleVersionId, scopeCode, scopeRevision.Id, result.Status);
            PpapWorkingFinding[] current = await dbContext.PpapWorkingFindings
                .Where(x => x.TenantId == tenantId
                    && x.SubmissionId == submission.Id
                    && x.RuleVersionId == result.RuleVersionId
                    && x.SubmissionDocumentId == scopeDocumentId
                    && x.IsCurrent)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            bool alreadyRecorded = false;
            foreach (PpapWorkingFinding finding in current)
            {
                if (isFinding && string.Equals(finding.InputFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    alreadyRecorded = true;
                    continue;
                }
                finding.Supersede(utcNow);
            }
            if (!isFinding || alreadyRecorded) continue;

            dbContext.PpapWorkingFindings.Add(PpapWorkingFinding.Create(
                tenantId,
                submission.Id,
                validationRunId,
                result.RuleVersionId,
                scopeDocumentId,
                scopeRevision.Id,
                Truncate(result.StableRuleCode, PpapWorkingFinding.StableRuleCodeMaxLength),
                Truncate(result.RuleName, PpapWorkingFinding.RuleNameMaxLength),
                result.Status == RuleEvaluationStatus.Failed ? WorkingFindingStatus.Failed : WorkingFindingStatus.Error,
                Truncate(BuildMessage(result), PpapWorkingFinding.MessageMaxLength),
                fingerprint,
                NormalizeEvidence(result.TraceJson)));
            created++;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        LogCompleted(logger, submissionId, revisionId, targetCode, results.Count, created);
    }

    internal static string ComputeFingerprint(Guid submissionId, Guid ruleVersionId, string documentCode, Guid revisionId, RuleEvaluationStatus status)
    {
        string input = string.Create(
            CultureInfo.InvariantCulture,
            $"draft-rule|{submissionId:N}|{ruleVersionId:N}|{documentCode}|{revisionId:N}|{status}");
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }

    internal static string BuildMessage(RuleEvaluationPreviewResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Message)) return result.Message;
        if (!string.IsNullOrWhiteSpace(result.Reason)) return $"{result.RuleName}: {result.Reason}";
        return result.Status == RuleEvaluationStatus.Failed
            ? $"{result.RuleName} failed."
            : $"{result.RuleName} could not be evaluated.";
    }

    private static string NormalizeEvidence(string traceJson)
    {
        if (string.IsNullOrWhiteSpace(traceJson)
            || Encoding.UTF8.GetByteCount(traceJson) > PpapWorkingFinding.MaximumEvidenceBytes
            || !traceJson.TrimStart().StartsWith('['))
        {
            return "[]";
        }
        return traceJson;
    }

    private static string Truncate(string value, int maximumLength)
    {
        string trimmed = value.Trim();
        return trimmed.Length <= maximumLength ? trimmed : trimmed[..maximumLength];
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped draft rule evaluation for submission {SubmissionId} revision {RevisionId}: {Reason}")]
    private static partial void LogSkipped(ILogger logger, Guid submissionId, Guid revisionId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Draft rule evaluation for submission {SubmissionId} revision {RevisionId} ({DocumentCode}) evaluated {ResultCount} rules and recorded {FindingCount} findings")]
    private static partial void LogCompleted(ILogger logger, Guid submissionId, Guid revisionId, string documentCode, int resultCount, int findingCount);
}
