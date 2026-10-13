using System.Net;
using DES.Modules.DocumentIntelligence.Providers;
using DES.Modules.Files.Contracts;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Providers;

public sealed class DocumentExtractionProviderRouterTests
{
    [Theory]
    [InlineData("DWG")]
    [InlineData("dwg")]
    [InlineData(" DWG ")]
    public async Task ExtractAsync_Should_SelectDrawingProvider_When_DocumentCodeIsDwg(string documentCode)
    {
        DocumentExtractionProviderRouter router = CreateRouter();

        ExtractionProviderResult result = await router.ExtractAsync(CreateRequest(documentCode));

        result.ErrorMessage.ShouldBe("Diagram drawing extraction is disabled.");
    }

    [Theory]
    [InlineData("PSW")]
    [InlineData("PPAP-4")]
    [InlineData("DRAWING")]
    public async Task ExtractAsync_Should_SelectAspectProvider_When_DocumentCodeIsNotDwg(string documentCode)
    {
        DocumentExtractionProviderRouter router = CreateRouter();

        ExtractionProviderResult result = await router.ExtractAsync(CreateRequest(documentCode));

        result.ErrorMessage.ShouldBe("ASPECT PDF extraction is disabled.");
    }

    private static DocumentExtractionProviderRouter CreateRouter()
    {
        IInternalFileArtifactService artifacts = Substitute.For<IInternalFileArtifactService>();
        HttpClient aspectClient = new(new NeverCalledHandler()) { BaseAddress = new Uri("https://aspect.test/") };
        HttpClient drawingClient = new(new NeverCalledHandler()) { BaseAddress = new Uri("https://diagram.test/") };
        AspectPdfExtractionProvider aspect = new(aspectClient, artifacts, Options.Create(new AspectPdfExtractionOptions()));
        DiagramDrawingExtractionProvider drawing = new(drawingClient, artifacts, Options.Create(new DiagramDrawingExtractionOptions()));
        return new DocumentExtractionProviderRouter(aspect, drawing);
    }

    private static ExtractionProviderRequest CreateRequest(string documentCode) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        documentCode,
        "model");

    private sealed class NeverCalledHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
    }
}
