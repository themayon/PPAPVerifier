namespace DES.Modules.Ppap.Contracts.v1.Workspace;

public sealed record DocumentRevisionDto(
    Guid Id,
    int RevisionNumber,
    long Version,
    Guid? SourceFileAssetId,
    string SourceFileName,
    string SourceContentType,
    long SourceSizeBytes,
    string UploadStatus,
    string ProcessingStatus,
    Guid? PreviewPdfFileAssetId,
    Guid? GeneratedWorkbookFileAssetId,
    Guid? CanonicalDocumentId,
    string? Provider,
    string? ExtractorVersion,
    string? FailureCode,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    DateTime? CompletedAtUtc);

public sealed record WorkspaceDocumentDto(
    Guid Id,
    string DocumentCode,
    bool IsRequired,
    string Disposition,
    string Status,
    Guid? FileAssetId,
    string? NotApplicableReason,
    DocumentRevisionDto? CurrentRevision,
    IReadOnlyList<DocumentRevisionDto> Revisions);

public sealed record WorkingFindingDto(
    Guid Id,
    Guid ValidationRunId,
    Guid RuleVersionId,
    Guid? SubmissionDocumentId,
    Guid? DocumentRevisionId,
    string StableRuleCode,
    string RuleName,
    string Status,
    string Message,
    string InputFingerprint,
    string EvidenceJson,
    DateTime CreatedAtUtc);

public sealed record SubmissionWorkspaceDto(
    Guid SubmissionId,
    string PartName,
    string PartNumber,
    string DrawingRevision,
    int PpapLevel,
    string Status,
    int Version,
    IReadOnlyList<WorkspaceDocumentDto> Documents,
    IReadOnlyList<WorkingFindingDto> Findings,
    IReadOnlyList<WaiverRequestDto> WaiverRequests);
