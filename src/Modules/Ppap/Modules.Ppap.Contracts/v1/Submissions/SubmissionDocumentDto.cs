using DES.Modules.Ppap.Contracts;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record SubmissionDocumentDto(
    Guid Id,
    Guid SubmissionId,
    string DocumentCode,
    bool IsRequired,
    SubmissionDocumentDisposition Disposition,
    string Status,
    Guid? FileAssetId,
    string? NotApplicableReason,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);
