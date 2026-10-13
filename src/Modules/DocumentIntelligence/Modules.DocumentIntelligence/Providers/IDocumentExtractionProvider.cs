namespace DES.Modules.DocumentIntelligence.Providers;

internal interface IDocumentExtractionProvider
{
    string ProviderKey { get; }
    Task<ExtractionProviderResult> ExtractAsync(
        ExtractionProviderRequest request,
        CancellationToken cancellationToken = default);
}

internal sealed record ExtractionProviderRequest(
    Guid ExtractionAttemptId,
    Guid OwnerUserId,
    Guid SourceFileAssetId,
    string DocumentCode,
    string? Model);

internal sealed record ExtractionProviderResult(
    ExtractionProviderOutcome Outcome,
    string? ProviderRequestId,
    string? SchemaVersion,
    string? CanonicalJson,
    Guid? RawResponseFileAssetId,
    Guid? GeneratedExcelFileAssetId,
    AspectProcessingMetadata? ProcessingMetadata,
    string? ErrorCode,
    string? ErrorMessage);

internal sealed record AspectProcessingMetadata(
    string? Coversheet,
    IReadOnlyDictionary<string, string> Metrics);

internal enum ExtractionProviderOutcome
{
    Succeeded,
    InvalidDocument,
    Failed
}
