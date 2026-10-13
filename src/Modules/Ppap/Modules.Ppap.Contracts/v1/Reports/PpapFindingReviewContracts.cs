using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Reports;

public enum PpapFindingKind
{
    Automated = 0,
    Manual = 1
}

public sealed record GetPpapReportReviewQuery(Guid Id) : IQuery<PpapReportDto>;

public sealed record CreateManualPpapFindingCommand(
    Guid ReportId,
    string Message,
    string? EvidenceNote,
    Guid? SubmissionDocumentId,
    Guid? CanonicalDocumentId,
    Guid? ExtractionAttemptId) : ICommand<Guid>;

public sealed record WaivePpapFindingCommand(
    Guid ReportId,
    PpapFindingKind FindingKind,
    Guid FindingId,
    string Reason) : ICommand<Guid>;

public sealed record RevokePpapFindingWaiverCommand(
    Guid ReportId,
    Guid WaiverId,
    string Reason) : ICommand<Guid>;

public sealed record PpapWaiverRevocationDto(
    Guid Id,
    Guid RevokedByUserId,
    string Reason,
    DateTime RevokedAtUtc);

public sealed record PpapFindingWaiverDto(
    Guid Id,
    bool IsActive,
    Guid ApprovedByUserId,
    string Reason,
    DateTime ApprovedAtUtc,
    PpapWaiverRevocationDto? Revocation);

public sealed record PpapManualFindingDto(
    Guid Id,
    Guid? SubmissionDocumentId,
    Guid? CanonicalDocumentId,
    Guid? ExtractionAttemptId,
    string? DocumentCode,
    string Message,
    string? EvidenceNote,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    IReadOnlyList<PpapFindingWaiverDto> Waivers);
