namespace DES.Modules.Ppap.Contracts.v1.Reports;

public enum PpapFindingOutcome
{
    Failed,
    NotEvaluated,
    Error
}

public sealed record PpapFindingDto(
    Guid Id,
    Guid RuleEvaluationRunId,
    Guid RuleEvaluationResultId,
    Guid TemplateVersionId,
    Guid RuleDefinitionId,
    Guid RuleVersionId,
    Guid? SubmissionDocumentId,
    Guid? CanonicalDocumentId,
    Guid? ExtractionAttemptId,
    string StableRuleCode,
    string RuleName,
    string? DocumentCode,
    PpapFindingOutcome Outcome,
    string? Message,
    string? Reason,
    string TraceJson,
    DateTime EvaluatedAtUtc)
{
    public IReadOnlyList<PpapFindingWaiverDto> Waivers { get; init; } = [];
}

public sealed record PpapReproducibilityManifestDto(
    string SchemaVersion,
    Guid JobId,
    Guid SubmissionId,
    int SubmissionVersion,
    Guid TemplateVersionId,
    PpapManifestEvaluationDto Evaluation,
    IReadOnlyList<PpapManifestDocumentDto> Documents);

public sealed record PpapManifestEvaluationDto(
    Guid RunId,
    long Version,
    string InputSchemaVersion,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    IReadOnlyList<PpapManifestEvaluationResultDto> Results);

public sealed record PpapManifestEvaluationResultDto(
    Guid Id,
    Guid RuleDefinitionId,
    Guid RuleVersionId,
    string StableRuleCode,
    string? DocumentCode,
    string Status,
    DateTime EvaluatedAtUtc);

public sealed record PpapManifestDocumentDto(
    Guid Id,
    string DocumentCode,
    string Disposition,
    string Status,
    Guid? SourceFileId,
    string? NotApplicableReason,
    PpapManifestExtractionProvenanceDto? Extraction);

public sealed record PpapManifestExtractionProvenanceDto(
    string SourceSha256,
    Guid AttemptId,
    string Provider,
    string Model,
    string ExtractorVersion,
    Guid CanonicalDocumentId,
    string CanonicalSchemaVersion,
    Guid? RawResponseArtifactId,
    Guid? GeneratedExcelArtifactId,
    DateTime CreatedAtUtc,
    DateTime CompletedAtUtc);

public sealed record PpapReportDto(
    Guid Id,
    Guid SubmissionId,
    Guid JobId,
    int SubmissionVersion,
    DateTime GeneratedAtUtc,
    string PartName,
    string PartNumber,
    string DrawingRevision,
    int AvailableDocumentCount,
    int NotApplicableDocumentCount,
    int QuarantinedDocumentCount,
    int FindingCount,
    int FailedFindingCount,
    int NotEvaluatedFindingCount,
    int ErrorFindingCount,
    IReadOnlyList<PpapFindingDto> Findings,
    PpapReproducibilityManifestDto? ReproducibilityManifest)
{
    public int AutomatedFindingCount { get; init; }
    public int ManualFindingCount { get; init; }
    public int WaivedFindingCount { get; init; }
    public int OpenFindingCount { get; init; }
    public IReadOnlyList<PpapManualFindingDto> ManualFindings { get; init; } = [];
    public PpapReportSubmissionStateDto? Submission { get; init; }
}

public sealed record PpapReportSubmissionStateDto(
    Guid Id,
    string Status,
    string Decision,
    string? DecisionComment,
    Guid? DecidedByUserId,
    DateTime? DecidedAtUtc,
    int Version,
    bool IsLatestReport);
