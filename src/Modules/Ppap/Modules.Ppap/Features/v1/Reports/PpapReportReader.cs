using System.Text.Json;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Reports;

internal static class PpapReportReader
{
    internal static async Task<PpapReportDto> ReadAsync(
        PpapDbContext dbContext,
        Guid reportId,
        string tenantId,
        Guid? ownerUserId,
        CancellationToken cancellationToken)
    {
        var reportData = await dbContext.PpapReports
            .AsNoTracking()
            .Where(report => report.TenantId == tenantId
                && report.Id == reportId
                && (ownerUserId == null || report.OwnerUserId == ownerUserId))
            .Join(
                dbContext.PpapJobs.AsNoTracking().Where(job => job.TenantId == tenantId && job.Status == PpapJobStatus.Completed),
                report => report.JobId,
                job => job.Id,
                (report, job) => report)
            .Join(
                dbContext.PpapSubmissions.AsNoTracking().Where(submission => submission.TenantId == tenantId
                    && (ownerUserId == null || submission.OwnerUserId == ownerUserId)),
                report => report.SubmissionId,
                submission => submission.Id,
                (report, submission) => new
                {
                    Report = report,
                    submission.PartName,
                    submission.PartNumber,
                    submission.DrawingRevision
                })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Completed PPAP report {reportId} not found.");

        PpapFinding[] automated = await dbContext.PpapFindings
            .AsNoTracking()
            .Where(finding => finding.TenantId == tenantId
                && finding.JobId == reportData.Report.JobId
                && finding.SubmissionId == reportData.Report.SubmissionId
                && finding.OwnerUserId == reportData.Report.OwnerUserId)
            .OrderBy(finding => finding.EvaluatedAtUtc)
            .ThenBy(finding => finding.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        PpapManualFinding[] manual = await dbContext.PpapManualFindings
            .AsNoTracking()
            .Where(finding => finding.TenantId == tenantId
                && finding.ReportId == reportData.Report.Id
                && finding.JobId == reportData.Report.JobId
                && finding.SubmissionId == reportData.Report.SubmissionId
                && finding.OwnerUserId == reportData.Report.OwnerUserId)
            .OrderBy(finding => finding.CreatedAtUtc)
            .ThenBy(finding => finding.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        PpapFindingWaiver[] waivers = await dbContext.PpapFindingWaivers
            .AsNoTracking()
            .Where(waiver => waiver.TenantId == tenantId && waiver.ReportId == reportData.Report.Id)
            .OrderBy(waiver => waiver.ApprovedAtUtc)
            .ThenBy(waiver => waiver.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        Guid[] waiverIds = waivers.Select(x => x.Id).ToArray();
        PpapWaiverRevocation[] revocations = await dbContext.PpapWaiverRevocations
            .AsNoTracking()
            .Where(revocation => revocation.TenantId == tenantId && waiverIds.Contains(revocation.WaiverId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, PpapWaiverRevocation> revocationByWaiver = revocations.ToDictionary(x => x.WaiverId);

        IReadOnlyList<PpapFindingWaiverDto> MapWaivers(PpapFindingTargetType targetType, Guid targetId) => waivers
            .Where(x => x.TargetType == targetType && x.TargetFindingId == targetId)
            .Select(x => new PpapFindingWaiverDto(
                x.Id,
                x.IsActive,
                x.ApprovedByUserId,
                x.Reason,
                x.ApprovedAtUtc,
                revocationByWaiver.TryGetValue(x.Id, out PpapWaiverRevocation? revocation)
                    ? new PpapWaiverRevocationDto(revocation.Id, revocation.RevokedByUserId, revocation.Reason, revocation.RevokedAtUtc)
                    : null))
            .ToArray();

        PpapFindingDto[] automatedDtos = automated.Select(finding => new PpapFindingDto(
            finding.Id,
            finding.RuleEvaluationRunId,
            finding.RuleEvaluationResultId,
            finding.TemplateVersionId,
            finding.RuleDefinitionId,
            finding.RuleVersionId,
            finding.SubmissionDocumentId,
            finding.CanonicalDocumentId,
            finding.ExtractionAttemptId,
            finding.StableRuleCode,
            finding.RuleName,
            finding.DocumentCode,
            finding.Outcome,
            finding.Message,
            finding.Reason,
            finding.TraceJson,
            finding.EvaluatedAtUtc)
        {
            Waivers = MapWaivers(PpapFindingTargetType.Automated, finding.Id)
        }).ToArray();
        PpapManualFindingDto[] manualDtos = manual.Select(finding => new PpapManualFindingDto(
            finding.Id,
            finding.SubmissionDocumentId,
            finding.CanonicalDocumentId,
            finding.ExtractionAttemptId,
            finding.DocumentCode,
            finding.Message,
            finding.EvidenceNote,
            finding.CreatedByUserId,
            finding.CreatedAtUtc,
            MapWaivers(PpapFindingTargetType.Manual, finding.Id))).ToArray();
        int manualCount = manualDtos.Length;
        int totalCount = reportData.Report.FindingCount + manualCount;
        int waivedCount = waivers.Count(x => x.IsActive);

        return new PpapReportDto(
            reportData.Report.Id,
            reportData.Report.SubmissionId,
            reportData.Report.JobId,
            reportData.Report.SubmissionVersion,
            reportData.Report.GeneratedAtUtc,
            reportData.PartName,
            reportData.PartNumber,
            reportData.DrawingRevision,
            reportData.Report.AvailableDocumentCount,
            reportData.Report.NotApplicableDocumentCount,
            reportData.Report.QuarantinedDocumentCount,
            totalCount,
            reportData.Report.FailedFindingCount,
            reportData.Report.NotEvaluatedFindingCount,
            reportData.Report.ErrorFindingCount,
            automatedDtos,
            DeserializeManifest(reportData.Report.ReproducibilityManifestJson))
        {
            AutomatedFindingCount = reportData.Report.FindingCount,
            ManualFindingCount = manualCount,
            WaivedFindingCount = waivedCount,
            OpenFindingCount = totalCount - waivedCount,
            ManualFindings = manualDtos
        };
    }

    private static PpapReproducibilityManifestDto? DeserializeManifest(string? manifestJson) =>
        manifestJson is null
            ? null
            : JsonSerializer.Deserialize<PpapReproducibilityManifestDto>(manifestJson, JsonSerializerOptions.Web)
                ?? throw new JsonException("The PPAP report reproducibility manifest is invalid.");
}
