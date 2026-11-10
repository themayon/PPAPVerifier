namespace DES.Modules.Ppap.Contracts.v1.Workspace;

public sealed record WaiverRequestDto(
    Guid Id,
    Guid SubmissionId,
    Guid WorkingFindingId,
    string FindingFingerprint,
    Guid RequestedByUserId,
    string RequestReason,
    string Status,
    Guid? ReviewedByUserId,
    string? ReviewReason,
    DateTime RequestedAtUtc,
    DateTime? ReviewedAtUtc,
    long Version);
