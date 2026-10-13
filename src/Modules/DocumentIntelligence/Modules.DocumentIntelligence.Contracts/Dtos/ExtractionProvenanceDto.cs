namespace DES.Modules.DocumentIntelligence.Contracts.Dtos;

public sealed record ExtractionProvenanceDto(
    Guid ExtractionAttemptId,
    Guid SubmissionDocumentId,
    Guid SourceFileAssetId,
    string DocumentCode,
    string SourceSha256,
    string Provider,
    string Model,
    string ExtractorVersion,
    string Status,
    Guid CanonicalDocumentId,
    string CanonicalSchemaVersion,
    Guid? RawResponseArtifactId,
    Guid? GeneratedExcelArtifactId,
    DateTime CreatedAtUtc,
    DateTime CompletedAtUtc);
