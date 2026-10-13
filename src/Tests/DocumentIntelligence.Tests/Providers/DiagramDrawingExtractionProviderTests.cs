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

public sealed class DiagramDrawingExtractionProviderTests
{
    [Fact]
    public async Task ExtractAsync_Should_SubmitPollPersistAndCanonicalize_When_TaskCompletes()
    {
        List<GeneratedFileArtifactRequest> stored = [];
        RecordingHandler handler = new((_, call) => call switch
        {
            1 => JsonResponse("{\"ref_id\":\"task-123\"}"),
            2 => JsonResponse("{\"task_id\":\"task-123\",\"status\":\"PENDING\"}"),
            3 => JsonResponse("{\"status\":\"PROCESSING\",\"task_id\":\"task-123\"}"),
            4 => JsonResponse(CompletedJson("/download-report/task-123")),
            _ => BytesResponse([1, 2, 3, 4])
        });
        DiagramDrawingExtractionOptions options = ValidOptions();
        options.ExternalTenantId = "tenant-7";
        options.ModelApiKey = "model-secret";
        options.DetectionModelApiKey = "detection-secret";
        options.DrawingDetectionProvider = "roboflow";
        options.DrawingDetectionModel = "detector-v2";
        options.DrawingDetectionApiKey = "drawing-detection-secret";
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts(stored), options);

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.Outcome.ShouldBe(ExtractionProviderOutcome.Succeeded);
        result.ProviderRequestId.ShouldBe("task-123");
        result.SchemaVersion.ShouldBe("drawing-1.0");
        result.RawResponseFileAssetId.ShouldNotBeNull();
        result.GeneratedExcelFileAssetId.ShouldNotBeNull();
        handler.Calls.ShouldBe(5);
        RecordedRequest submission = handler.Requests[0];
        submission.Method.ShouldBe(HttpMethod.Post);
        submission.Uri.ShouldBe(new Uri("https://diagram.test/api/upload-file"));
        submission.ApiKey.ShouldBe("drawing-api-key");
        submission.TenantId.ShouldBe("tenant-7");
        submission.ModelApiKey.ShouldBe("model-secret");
        submission.FormFields.Keys.OrderBy(value => value, StringComparer.Ordinal).ShouldBe([
            "detection_model_api_key",
            "disable_webhook",
            "drawing_detection_api_key",
            "drawing_detection_model",
            "drawing_detection_provider",
            "file",
            "model",
            "provider"
        ]);
        submission.FormFields["file"].ShouldBe("<file>");
        submission.FormFields["provider"].ShouldBe("gemini");
        submission.FormFields["model"].ShouldBe("request-model");
        submission.FormFields["disable_webhook"].ShouldBe("true");
        submission.FormFields["detection_model_api_key"].ShouldBe("detection-secret");
        submission.FormFields["drawing_detection_provider"].ShouldBe("roboflow");
        submission.FormFields["drawing_detection_model"].ShouldBe("detector-v2");
        submission.FormFields["drawing_detection_api_key"].ShouldBe("drawing-detection-secret");
        submission.Body.ShouldNotContain("external_tenant_id");
        submission.Body.ShouldNotContain("detection_model_path");
        handler.Requests.Skip(1).ShouldAllBe(item => item.ApiKey == "drawing-api-key");
        handler.Requests.Skip(1).ShouldAllBe(item => item.TenantId == null && item.ModelApiKey == null);
        stored.Count.ShouldBe(2);
        stored[0].FileName.ShouldEndWith("_drawing-response.json");
        stored[1].FileName.ShouldBe("task-123_drawing.xlsx");
        handler.Requests[4].Uri.ShouldBe(new Uri("https://diagram.test/download-report/task-123"));
        string raw = Encoding.UTF8.GetString(stored[0].Data);
        raw.ShouldNotContain("/private/provider/output.json");
        raw.ShouldNotContain("base64-image-data");
        raw.ShouldNotContain("provider-token");
        string canonical = result.CanonicalJson.ShouldNotBeNull();
        canonical.ShouldBe("{\"documentCode\":\"DWG\",\"drawing\":{\"Is_Valid\":\"Yes\",\"dimensions\":{\"height\":20,\"width\":10},\"name\":\"Drawing A\"},\"model\":\"request-model\",\"provider\":\"gemini\"}");
    }

    [Fact]
    public async Task ExtractAsync_Should_OmitOptionalMultipartFields_When_NotConfigured()
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"task-1\"}")
            : JsonResponse(CompletedJson(null, "task-1")));
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.Outcome.ShouldBe(ExtractionProviderOutcome.Succeeded);
        handler.Requests[0].TenantId.ShouldBeNull();
        handler.Requests[0].ModelApiKey.ShouldBeNull();
        handler.Requests[0].FormFields.Keys.OrderBy(value => value, StringComparer.Ordinal).ShouldBe([
            "disable_webhook",
            "file",
            "model",
            "provider"
        ]);
        handler.Requests[0].Body.ShouldNotContain("external_tenant_id");
        handler.Requests[0].Body.ShouldNotContain("detection_model_path");
    }

    [Fact]
    public async Task ExtractAsync_Should_ClassifyInvalidDocument_When_ResultSaysNo()
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"task-invalid\"}")
            : JsonResponse("{\"task_id\":\"task-invalid\",\"status\":\"COMPLETED\",\"result\":{\"Is_Valid\":\"No\"}}"));
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.Outcome.ShouldBe(ExtractionProviderOutcome.InvalidDocument);
        result.ErrorCode.ShouldBe("InvalidDocument");
        result.RawResponseFileAssetId.ShouldNotBeNull();
    }

    [Fact]
    public async Task ExtractAsync_Should_FailAndPersistRaw_When_TaskStatusIsError()
    {
        List<GeneratedFileArtifactRequest> stored = [];
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"task-error\"}")
            : JsonResponse("{\"task_id\":\"task-error\",\"status\":\"ERROR\",\"access_token\":\"secret\"}"));
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts(stored));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("ProviderError");
        result.RawResponseFileAssetId.ShouldNotBeNull();
        Encoding.UTF8.GetString(stored.Single().Data).ShouldNotContain("secret");
    }

    [Fact]
    public async Task ExtractAsync_Should_Timeout_When_TaskNeverCompletes()
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"task-timeout\"}")
            : JsonResponse("{\"task_id\":\"task-timeout\",\"status\":\"PENDING\"}"));
        DiagramDrawingExtractionOptions options = ValidOptions();
        options.TotalTimeoutMinutes = 0.01;
        options.PollIntervalSeconds = 0.01;
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]), options);

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("ProviderTimeout");
        handler.Calls.ShouldBeGreaterThan(1);
    }

    [Fact]
    public async Task ExtractAsync_Should_PropagateCallerCancellation()
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"task-cancel\"}")
            : JsonResponse("{\"task_id\":\"task-cancel\",\"status\":\"PENDING\"}"));
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => provider.ExtractAsync(CreateRequest(), cancellation.Token));
    }

    [Theory]
    [InlineData("../task")]
    [InlineData("https://evil.test/task")]
    [InlineData("task/child")]
    [InlineData("")]
    public async Task ExtractAsync_Should_RejectMaliciousRefId(string refId)
    {
        RecordingHandler handler = new((_, _) => JsonResponse($"{{\"ref_id\":\"{refId}\"}}"));
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("InvalidProviderResponse");
        handler.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task ExtractAsync_Should_RejectRefIdAsStatusCorrelationField()
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"expected\"}")
            : JsonResponse("{\"ref_id\":\"expected\",\"status\":\"COMPLETED\",\"result\":{\"Is_Valid\":\"Yes\"}}"));
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("TaskCorrelationMismatch");
    }

    [Fact]
    public async Task ExtractAsync_Should_RejectMismatchedTaskCorrelation()
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"expected\"}")
            : JsonResponse("{\"task_id\":\"other\",\"status\":\"COMPLETED\",\"result\":{\"Is_Valid\":\"Yes\"}}"));
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("TaskCorrelationMismatch");
    }

    [Theory]
    [InlineData("https://evil.test/download-report/task-url")]
    [InlineData("//evil.test/download-report/task-url")]
    [InlineData("/download-report/../task-url")]
    [InlineData("/download-report/%2e%2e/task-url")]
    [InlineData("/download-report/task-url?token=secret")]
    [InlineData("/download-report/other-task")]
    [InlineData("/reports/task-url")]
    public async Task ExtractAsync_Should_RejectUnsafeExcelUrl(string url)
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"task-url\"}")
            : JsonResponse(CompletedJson(url, "task-url")));
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("UnsafeDownloadUrl");
        handler.Calls.ShouldBe(2);
    }

    [Fact]
    public async Task ExtractAsync_Should_BoundEveryProviderBody()
    {
        RecordingHandler handler = new((_, _) => JsonResponse("{\"ref_id\":\"" + new string('x', 200) + "\"}"));
        DiagramDrawingExtractionOptions options = ValidOptions();
        options.MaxResponseBytes = 32;
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]), options);

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("ResponseTooLarge");
    }

    [Fact]
    public async Task ExtractAsync_Should_RejectCanonicalJsonThatExceedsBound()
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"task-large\"}")
            : JsonResponse("{\"task_id\":\"task-large\",\"status\":\"COMPLETED\",\"result\":{\"Is_Valid\":\"Yes\",\"value\":\"" + new string('x', 200) + "\"}}"));
        DiagramDrawingExtractionOptions options = ValidOptions();
        options.MaxCanonicalJsonBytes = 64;
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]), options);

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("CanonicalResponseTooLarge");
        result.RawResponseFileAssetId.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("drawing.pdf", "application/pdf")]
    [InlineData("drawing.jpg", "image/jpeg")]
    [InlineData("drawing.jpeg", "image/jpeg")]
    [InlineData("drawing.png", "image/png")]
    public async Task ExtractAsync_Should_AcceptSupportedSourceTypes(string fileName, string contentType)
    {
        RecordingHandler handler = new((_, call) => call == 1
            ? JsonResponse("{\"ref_id\":\"task-source\"}")
            : JsonResponse(CompletedJson(null, "task-source")));
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([], fileName, contentType));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.Outcome.ShouldBe(ExtractionProviderOutcome.Succeeded);
    }

    [Theory]
    [InlineData("drawing.gif", "image/gif")]
    [InlineData("drawing.pdf", "image/png")]
    public async Task ExtractAsync_Should_RejectUnsupportedOrMismatchedSourceTypes(string fileName, string contentType)
    {
        RecordingHandler handler = new((_, _) => throw new InvalidOperationException());
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([], fileName, contentType));

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("InvalidSourceType");
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task ExtractAsync_Should_RejectSourceThatExceedsBound()
    {
        RecordingHandler handler = new((_, _) => throw new InvalidOperationException());
        DiagramDrawingExtractionOptions options = ValidOptions();
        options.MaxSourceBytes = 2;
        DiagramDrawingExtractionProvider provider = CreateProvider(handler, CreateArtifacts([]), options);

        ExtractionProviderResult result = await provider.ExtractAsync(CreateRequest());

        result.ErrorCode.ShouldBe("SourceTooLarge");
        handler.Calls.ShouldBe(0);
    }

    private static DiagramDrawingExtractionProvider CreateProvider(
        RecordingHandler handler,
        IInternalFileArtifactService artifacts,
        DiagramDrawingExtractionOptions? configured = null)
    {
        HttpClient client = new(handler) { BaseAddress = new Uri("https://diagram.test/api/") };
        return new DiagramDrawingExtractionProvider(client, artifacts, Options.Create(configured ?? ValidOptions()));
    }

    private static IInternalFileArtifactService CreateArtifacts(
        List<GeneratedFileArtifactRequest> stored,
        string fileName = "drawing.pdf",
        string contentType = "application/pdf")
    {
        IInternalFileArtifactService artifacts = Substitute.For<IInternalFileArtifactService>();
        byte[] source = Encoding.UTF8.GetBytes("drawing");
        artifacts.OpenReadAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new InternalFileArtifactContent(new MemoryStream(source), contentType, fileName, source.Length));
        artifacts.CreatePrivateAsync(Arg.Any<GeneratedFileArtifactRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                GeneratedFileArtifactRequest request = call.Arg<GeneratedFileArtifactRequest>();
                stored.Add(request);
                return new GeneratedFileArtifactResult(Guid.CreateVersion7(), FileAssetStatus.Available, 0, request.Data.Length);
            });
        return artifacts;
    }

    private static DiagramDrawingExtractionOptions ValidOptions() => new()
    {
        Enabled = true,
        BaseUrl = "https://diagram.test/api/",
        ApiKey = "drawing-api-key",
        Provider = "gemini",
        Model = "drawing-model",
        PollIntervalSeconds = 0.001,
        TotalTimeoutMinutes = 1,
        LeaseMinutes = 2
    };

    private static ExtractionProviderRequest CreateRequest() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "DWG",
        "request-model");

    private static string CompletedJson(string? excelUrl = null, string taskId = "task-123")
    {
        string excel = excelUrl is null ? string.Empty : $",\"excel_download_url\":\"{excelUrl}\"";
        return $"{{\"task_id\":\"{taskId}\",\"status\":\"COMPLETED\"{excel},\"result\":{{\"name\":\"Drawing A\",\"Is_Valid\":\"Yes\",\"dimensions\":{{\"width\":10,\"height\":20}},\"local_path\":\"/private/provider/output.json\",\"image\":\"base64-image-data\",\"access_token\":\"provider-token\"}}}}";
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static HttpResponseMessage BytesResponse(byte[] bytes) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(bytes)
    };

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
            Dictionary<string, string> formFields = [];
            if (request.Content is MultipartFormDataContent multipart)
            {
                foreach (HttpContent content in multipart)
                {
                    string? name = content.Headers.ContentDisposition?.Name?.Trim('"');
                    if (name is not null)
                    {
                        formFields[name] = content.Headers.ContentDisposition?.FileName is null
                            ? await content.ReadAsStringAsync(cancellationToken)
                            : "<file>";
                    }
                }
            }
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                HeaderValue(request, "X-API-KEY"),
                HeaderValue(request, "X-Tenant-ID"),
                HeaderValue(request, "X-Model-Api-Key"),
                formFields,
                body.Replace('"', ' ')));
            return responder(request, Calls);
        }

        private static string? HeaderValue(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out IEnumerable<string>? values) ? values.Single() : null;
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? ApiKey,
        string? TenantId,
        string? ModelApiKey,
        IReadOnlyDictionary<string, string> FormFields,
        string Body);
}
