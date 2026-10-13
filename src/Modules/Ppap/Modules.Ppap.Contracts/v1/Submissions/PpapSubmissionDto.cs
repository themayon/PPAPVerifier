namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record PpapSubmissionDto(
    Guid Id,
    Guid? PartId,
    string PartName,
    string PartNumber,
    string DrawingRevision,
    int PpapLevel,
    Guid? RuleTemplateId,
    Guid? RuleTemplateVersionId,
    string Status,
    int Version,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? SubmittedAtUtc,
    DateTime? CancelledAtUtc);
