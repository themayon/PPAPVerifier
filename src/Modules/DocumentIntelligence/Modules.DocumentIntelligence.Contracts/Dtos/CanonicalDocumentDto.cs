namespace DES.Modules.DocumentIntelligence.Contracts.Dtos;

public sealed record CanonicalDocumentDto(
    Guid Id,
    Guid ExtractionAttemptId,
    string SchemaVersion,
    string CanonicalJson,
    Guid? RawResponseFileAssetId,
    Guid? GeneratedExcelFileAssetId,
    DateTime CreatedAtUtc);
