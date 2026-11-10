using System.Text.Json;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Processing;

internal sealed class ComplexValidationResultProcessor(PpapDbContext dbContext)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(
        ComplexValidationCompletedIntegrationEvent result,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);
        bool duplicate = await dbContext.PpapWorkingFindings.AnyAsync(
            finding => finding.SubmissionId == result.SubmissionId
                && finding.ValidationRunId == result.ValidationRunId
                && finding.RuleVersionId == result.RuleVersionId
                && finding.InputFingerprint == result.InputFingerprint
                && finding.IsCurrent,
            cancellationToken).ConfigureAwait(false);
        if (duplicate) return;

        PpapWorkingFinding[] stale = await dbContext.PpapWorkingFindings
            .Where(finding => finding.SubmissionId == result.SubmissionId
                && finding.RuleVersionId == result.RuleVersionId
                && finding.IsCurrent)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (PpapWorkingFinding finding in stale) finding.Supersede(result.OccurredOnUtc);
        if (string.Equals(result.Status, "Passed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(result.Status, "NotEvaluated", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        WorkingFindingStatus status = string.Equals(result.Status, "Failed", StringComparison.OrdinalIgnoreCase)
            ? WorkingFindingStatus.Failed
            : WorkingFindingStatus.Error;
        ComplexValidationEvidence? primaryEvidence = result.Evidence.Count > 0
            ? result.Evidence[0]
            : null;
        PpapWorkingFinding created = PpapWorkingFinding.Create(
            result.TenantId,
            result.SubmissionId,
            result.ValidationRunId,
            result.RuleVersionId,
            primaryEvidence?.SubmissionDocumentId,
            primaryEvidence?.DocumentRevisionId,
            result.StableRuleCode,
            result.RuleName,
            status,
            string.IsNullOrWhiteSpace(result.Message) ? "Complex validation failed without a message." : result.Message,
            result.InputFingerprint,
            JsonSerializer.Serialize(result.Evidence, JsonOptions));
        dbContext.PpapWorkingFindings.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
