namespace DES.Modules.DocumentIntelligence.Providers;

internal sealed class DocumentExtractionProviderRouter(
    AspectPdfExtractionProvider aspectProvider,
    DiagramDrawingExtractionProvider drawingProvider) : IDocumentExtractionProvider
{
    public string ProviderKey => "document-code-router";

    public Task<ExtractionProviderResult> ExtractAsync(
        ExtractionProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        IDocumentExtractionProvider provider = string.Equals(request.DocumentCode.Trim(), "DWG", StringComparison.OrdinalIgnoreCase)
            ? drawingProvider
            : aspectProvider;
        return provider.ExtractAsync(request, cancellationToken);
    }
}
