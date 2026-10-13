namespace DES.Modules.Ppap.Contracts.v1.Jobs;

public sealed record PpapAdminJobDto(
    Guid Id,
    Guid SubmissionId,
    int SubmissionVersion,
    string PartName,
    string PartNumber,
    Guid OwnerUserId,
    string Status,
    string CurrentStage,
    int ProgressPercent,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? FailedAtUtc,
    string? ErrorCode,
    string? ErrorMessage,
    Guid? ReportId);
