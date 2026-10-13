using System.IO.Compression;
using System.Net;
using System.Text;
using DES.Modules.DocumentIntelligence.Providers;
using DES.Modules.Files.Contracts;
using DES.Modules.Files.Contracts.v1.DTOs;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Providers;

public sealed class AspectPdfExtractionProviderTests
{
    [Fact]
    public async Task ExtractAsync_Should_DownloadStoreAndCanonicalize_When_ResponseIsSuccessful()
    {
        byte[] workbook = CreateWorkbook("<c r=\"A1\" t=\"inlineStr\"><is><t>Evidence</t></is></c><c r=\"B2\"><v>42</v></c>");
        RecordingHandler handler = new((request, call) => call == 1
            ? JsonResponse(SuccessJson("result.xlsx", "/download_excel/result.xlsx"))
            : BytesResponse(workbook));
        List<GeneratedFileArtifactRequest> stored = [];
        IInternalFileArtifactService artifacts = CreateArtifacts(stored);
        AspectPdfExtractionProvider provider = CreateProvider(handler, artifacts);

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest("request-model"));

        result.Outcome.ShouldBe(ExtractionProviderOutcome.Succeeded);
        result.GeneratedExcelFileAssetId.ShouldNotBeNull();
        result.RawResponseFileAssetId.ShouldNotBeNull();
        string canonicalJson = result.CanonicalJson.ShouldNotBeNull();
        canonicalJson.ShouldContain("Evidence");
        canonicalJson.ShouldContain("\"row\":2");
        result.ProcessingMetadata!.Metrics["total_tokens"].ShouldBe("2");
        handler.Calls.ShouldBe(2);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Post);
        handler.Requests[0].Uri.ShouldBe(new Uri("https://aspect.test/api/extract_pdf"));
        handler.Requests[0].ApiKey.ShouldBe("test-api-key");
        handler.Requests[0].Accept.ShouldBe("application/json");
        handler.Requests[0].Body.ShouldContain("name=file");
        handler.Requests[0].Body.ShouldContain("name=doc_type");
        handler.Requests[0].Body.ShouldContain("PPAP-4");
        handler.Requests[0].Body.ShouldContain("name=generate_excel");
        handler.Requests[0].Body.ShouldContain("true");
        handler.Requests[0].Body.ShouldContain("name=provider");
        handler.Requests[0].Body.ShouldContain("gemini");
        handler.Requests[0].Body.ShouldContain("name=model");
        handler.Requests[0].Body.ShouldContain("request-model");
        handler.Requests[0].Body.ShouldNotContain("TemplateDocId");
        handler.Requests[0].Body.ShouldNotContain("standard_pdf");
        handler.Requests[0].Body.ShouldNotContain("knowledge_store_api_key");
        stored.Count.ShouldBe(2);
        stored[0].OwnerType.ShouldBe("DocumentExtraction");
        stored[0].FileName.ShouldEndWith(".json");
        stored[1].FileName.ShouldBe("result.xlsx");
    }

    [Theory]
    [InlineData("No", "No", "InvalidDocument")]
    [InlineData("Yes", "Yes", "CoversheetDocument")]
    public async Task ExtractAsync_Should_NotDownload_When_DocumentIsInvalid(
        string isValid,
        string coversheet,
        string expectedCode)
    {
        string json = SuccessJson("result.xlsx", "/download_excel/result.xlsx")
            .Replace("\"Is_Valid\":\"Yes\"", $"\"Is_Valid\":\"{isValid}\"", StringComparison.Ordinal)
            .Replace("\"coversheet\":\"No\"", $"\"coversheet\":\"{coversheet}\"", StringComparison.Ordinal);
        RecordingHandler handler = new((_, _) => JsonResponse(json));
        AspectPdfExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.Outcome.ShouldBe(ExtractionProviderOutcome.InvalidDocument);
        result.ErrorCode.ShouldBe(expectedCode);
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ExtractAsync_Should_ClassifySecuredCoversheetResponseAsInvalid()
    {
        RecordingHandler handler = new((_, _) => JsonResponse(
            "{\"Is_Valid\":\"No\",\"coversheet_msg\":\"Yes\",\"error\":\"Coversheet-only document is not valid for PPAP review.\"}"));
        AspectPdfExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.Outcome.ShouldBe(ExtractionProviderOutcome.InvalidDocument);
        result.ErrorCode.ShouldBe("CoversheetDocument");
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ExtractAsync_Should_Fail_When_Http200ContainsError()
    {
        RecordingHandler handler = new((_, _) => JsonResponse("{\"error\":\"provider failed\"}"));
        AspectPdfExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.Outcome.ShouldBe(ExtractionProviderOutcome.Failed);
        result.ErrorCode.ShouldBe("ProviderError");
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ExtractAsync_Should_NotRetry_When_StatusIsPermanent()
    {
        RecordingHandler handler = new((_, _) => JsonResponse("{\"error\":\"bad request\"}", HttpStatusCode.BadRequest));
        AspectPdfExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("ProviderBadRequest");
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ExtractAsync_Should_RetryTransientStatus_When_RequestEventuallySucceeds()
    {
        byte[] workbook = CreateWorkbook("<c r=\"A1\"><v>1</v></c>");
        RecordingHandler handler = new((_, call) => call switch
        {
            1 => JsonResponse("{\"error\":\"busy\"}", HttpStatusCode.ServiceUnavailable),
            2 => JsonResponse(SuccessJson("result.xlsx", "/download_excel/result.xlsx")),
            _ => BytesResponse(workbook)
        });
        AspectPdfExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.Outcome.ShouldBe(ExtractionProviderOutcome.Succeeded);
        handler.Calls.ShouldBe(3);
    }

    [Theory]
    [InlineData("https://evil.test/download_excel/result.xlsx")]
    [InlineData("/download_excel/../result.xlsx")]
    [InlineData("/download_excel/result.xlsx?token=x")]
    [InlineData("/other/result.xlsx")]
    public async Task ExtractAsync_Should_RejectUnsafeDownloadUrl(string downloadUrl)
    {
        RecordingHandler handler = new((_, _) => JsonResponse(SuccessJson("result.xlsx", downloadUrl)));
        AspectPdfExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("UnsafeDownloadUrl");
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ExtractAsync_Should_Fail_When_JsonExceedsLimit()
    {
        RecordingHandler handler = new((_, _) => JsonResponse("{\"value\":\"" + new string('x', 200) + "\"}"));
        AspectPdfExtractionOptions options = ValidOptions();
        options.MaxResponseBytes = 32;
        AspectPdfExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]), options);

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("ResponseTooLarge");
    }

    [Fact]
    public async Task ExtractAsync_Should_Fail_When_ExcelExceedsLimit()
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse(SuccessJson("result.xlsx", "/download_excel/result.xlsx"))
            : BytesResponse(new byte[100]));
        AspectPdfExtractionOptions options = ValidOptions();
        options.MaxExcelBytes = 32;
        AspectPdfExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]), options);

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("ExcelTooLarge");
    }

    [Fact]
    public async Task ExtractAsync_Should_Fail_When_DownloadIsNotXlsx()
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse(SuccessJson("result.xlsx", "/download_excel/result.xlsx"))
            : BytesResponse(Encoding.UTF8.GetBytes("not a zip")));
        AspectPdfExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("InvalidExcel");
    }

    [Theory]
    [InlineData("source.txt", "application/pdf")]
    [InlineData("source.pdf", "text/plain")]
    public async Task ExtractAsync_Should_FailBeforeHttp_When_SourceIsNotPdf(string fileName, string contentType)
    {
        RecordingHandler handler = new((_, _) => throw new InvalidOperationException());
        IInternalFileArtifactService artifacts = Substitute.For<IInternalFileArtifactService>();
        artifacts.OpenReadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new InternalFileArtifactContent(new MemoryStream([1, 2, 3]), contentType, fileName, 3));
        AspectPdfExtractionProvider provider = CreateProvider(handler, artifacts);

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("InvalidSourceType");
        handler.Calls.ShouldBe(0);
    }

    private static AspectPdfExtractionProvider CreateProvider(
        RecordingHandler handler,
        IInternalFileArtifactService artifacts,
        AspectPdfExtractionOptions? configured = null)
    {
        HttpClient client = new(handler) { BaseAddress = new Uri("https://aspect.test/api/") };
        return new AspectPdfExtractionProvider(client, artifacts, Options.Create(configured ?? ValidOptions()));
    }

    private static IInternalFileArtifactService CreateArtifacts(List<GeneratedFileArtifactRequest> stored)
    {
        IInternalFileArtifactService artifacts = Substitute.For<IInternalFileArtifactService>();
        artifacts.OpenReadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new InternalFileArtifactContent(
                new MemoryStream(Encoding.UTF8.GetBytes("%PDF-1.7")),
                "application/pdf",
                "unsafe source.pdf",
                8));
        artifacts.CreatePrivateAsync(Arg.Any<GeneratedFileArtifactRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                GeneratedFileArtifactRequest request = call.Arg<GeneratedFileArtifactRequest>();
                stored.Add(request);
                return new GeneratedFileArtifactResult(Guid.CreateVersion7(), FileAssetStatus.Available, 0, request.Data.Length);
            });
        return artifacts;
    }

    private static AspectPdfExtractionOptions ValidOptions() => new()
    {
        Enabled = true,
        BaseUrl = "https://aspect.test/api/",
        ApiKey = "test-api-key",
        Provider = "gemini",
        Model = "default-model",
        MaxRetryAttempts = 3,
        FirstRetryDelaySeconds = 0
    };

    private static ExtractionProviderRequest CreateRequest(string? model = null) => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "PPAP-4",
        model);

    private static string SuccessJson(string fileName, string downloadUrl) =>
        "{\"Is_Valid\":\"Yes\",\"process_id\":\"" + Guid.CreateVersion7()
        + "\",\"coversheet\":\"No\",\"processing_summary\":{\"total_tokens\":2},\"excel_info\":{\"excel_generated\":true,\"excel_filename\":\""
        + fileName + "\",\"excel_download_url\":\"" + downloadUrl + "\"}}";

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes)
    };

    internal static byte[] CreateWorkbook(string cells, string? sharedStrings = null)
    {
        using MemoryStream output = new();
        using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Sheet1\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            AddEntry(archive, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            AddEntry(archive, "xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\">{cells}</row></sheetData></worksheet>");
            if (sharedStrings is not null)
            {
                AddEntry(archive, "xl/sharedStrings.xml", sharedStrings);
            }
        }

        return output.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string path, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(path);
        using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, int, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("X-API-Key", out IEnumerable<string>? keys) ? keys.Single() : null,
                request.Headers.Accept.SingleOrDefault()?.MediaType,
                body.Replace('"', ' ')));
            return responder(request, Calls);
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? ApiKey, string? Accept, string Body);
}
