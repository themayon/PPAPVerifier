namespace DES.Modules.Ppap.Contracts.v1.Jobs;

public sealed record PpapJobDto(
    Guid Id,
    Guid SubmissionId,
    int SubmissionVersion,
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
