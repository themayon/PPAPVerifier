using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using DES.Modules.Files.Contracts;
using DES.Modules.Files.Contracts.v1.DTOs;
using Microsoft.Extensions.Options;

namespace DES.Modules.DocumentIntelligence.Providers;

internal sealed class AspectPdfExtractionProvider(
    HttpClient httpClient,
    IInternalFileArtifactService artifacts,
    IOptions<AspectPdfExtractionOptions> options) : IDocumentExtractionProvider
{
    private const string OwnerType = "DocumentExtraction";
    private static readonly HashSet<HttpStatusCode> TransientStatuses =
    [
        HttpStatusCode.RequestTimeout,
        HttpStatusCode.TooManyRequests,
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout
    ];
    private readonly AspectPdfExtractionOptions _options = options.Value;

    public string ProviderKey => "aspect-pdf";

    public async Task<ExtractionProviderResult> ExtractAsync(
        ExtractionProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.Enabled)
        {
            return Failed("ProviderDisabled", "ASPECT PDF extraction is disabled.");
        }

        if (request.ExtractionAttemptId == Guid.Empty || request.OwnerUserId == Guid.Empty
            || request.SourceFileAssetId == Guid.Empty)
        {
            return Failed("InvalidRequest", "Extraction identifiers must be non-empty.");
        }

        byte[] sourceBytes;
        string sourceFileName;
        await using (InternalFileArtifactContent source = await artifacts.OpenReadAsync(
            request.SourceFileAssetId,
            request.OwnerUserId.ToString(),
            cancellationToken).ConfigureAwait(false))
        {
            string extension = Path.GetExtension(source.FileName).ToUpperInvariant();
            bool supportedAsyncSource = extension is ".PDF" or ".DOCX" or ".XLSX" or ".PPTX";
            if ((_options.UseAsyncTaskApi && !supportedAsyncSource)
                || (!_options.UseAsyncTaskApi
                    && (!string.Equals(extension, ".PDF", StringComparison.Ordinal)
                        || !string.Equals(source.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))))
            {
                return Failed("InvalidSourceType", _options.UseAsyncTaskApi
                    ? "The source artifact must be a PDF, DOCX, XLSX, or PPTX file."
                    : "The source artifact must be a PDF.");
            }

            if (source.SizeBytes > _options.MaxSourceBytes)
            {
                return Failed("SourceTooLarge", "The source PDF exceeds the configured size limit.");
            }

            try
            {
                sourceBytes = await ReadBoundedAsync(source.Content, _options.MaxSourceBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return Failed("SourceTooLarge", "The source PDF exceeds the configured size limit.");
            }

            sourceFileName = BuildSourceFileName(request.ExtractionAttemptId, source.FileName);
        }

        if (_options.UseAsyncTaskApi)
        {
            return await ExtractWithTaskApiAsync(
                request,
                sourceBytes,
                sourceFileName,
                cancellationToken).ConfigureAwait(false);
        }

        HttpResponseMessage extractionResponse;
        try
        {
            extractionResponse = await SendWithRetryAsync(
                () => CreateExtractionRequest(request, sourceBytes, sourceFileName),
                TransientStatuses,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return Failed("ProviderUnavailable", "The extraction provider could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("ProviderTimeout", "The extraction provider request timed out.");
        }

        using (extractionResponse)
        {
            byte[] responseBytes;
            try
            {
                responseBytes = await ReadResponseBoundedAsync(
                    extractionResponse,
                    _options.MaxResponseBytes,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return Failed("ResponseTooLarge", "The provider response exceeds the configured size limit.");
            }

            JsonNode responseNode;
            try
            {
                responseNode = JsonNode.Parse(responseBytes)
                    ?? throw new JsonException("The provider response is empty.");
            }
            catch (JsonException)
            {
                return Failed("InvalidProviderResponse", "The extraction provider returned invalid JSON.");
            }

            byte[] sanitizedResponse = SanitizeResponse(responseNode);
            GeneratedFileArtifactResult rawArtifact = await artifacts.CreatePrivateAsync(
                new GeneratedFileArtifactRequest(
                    OwnerType,
                    request.ExtractionAttemptId,
                    $"{request.ExtractionAttemptId:N}_aspect-response.json",
                    "application/json",
                    sanitizedResponse),
                request.OwnerUserId.ToString(),
                cancellationToken).ConfigureAwait(false);
            if (rawArtifact.FileAssetStatus != FileAssetStatus.Available)
            {
                return Failed("RawResponseUnavailable", "The provider response artifact was not available.");
            }

            using JsonDocument responseDocument = JsonDocument.Parse(sanitizedResponse);
            JsonElement root = responseDocument.RootElement;
            string? processId = ScalarString(FindProperty(root, "process_id"));
            string? coversheet = ScalarString(FindPropertyRecursive(root, "coversheet_msg"))
                ?? ScalarString(FindPropertyRecursive(root, "coversheet"))
                ?? ScalarString(FindPropertyRecursive(root, "is_coversheet"));
            AspectProcessingMetadata metadata = new(coversheet, ReadMetrics(root));

            if (extractionResponse.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
            {
                return FailedWithRaw(
                    rawArtifact.FileAssetId,
                    processId,
                    metadata,
                    extractionResponse.StatusCode == HttpStatusCode.RequestEntityTooLarge ? "ProviderPayloadTooLarge" : "ProviderBadRequest",
                    "The extraction provider rejected the request.");
            }

            if (!extractionResponse.IsSuccessStatusCode)
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "ProviderHttpError", "The extraction provider returned an unsuccessful status.");
            }

            if (IsNo(ScalarString(FindProperty(root, "Is_Valid"))))
            {
                string code = IsYes(coversheet) ? "CoversheetDocument" : "InvalidDocument";
                string message = IsYes(coversheet)
                    ? "Coversheets cannot be extracted as PPAP documents."
                    : "The provider determined that the PDF is invalid.";
                return Invalid(rawArtifact.FileAssetId, processId, metadata, code, message);
            }

            string? providerError = ScalarString(FindProperty(root, "error"));
            if (!string.IsNullOrWhiteSpace(providerError))
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "ProviderError", "The extraction provider reported an error.");
            }

            if (IsYes(coversheet))
            {
                return Invalid(rawArtifact.FileAssetId, processId, metadata, "CoversheetDocument", "Coversheets cannot be extracted as PPAP documents.");
            }

            if (!IsYes(ScalarString(FindProperty(root, "Is_Valid"))))
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "InvalidProviderResponse", "The provider did not confirm document validity.");
            }

            if (!Guid.TryParse(processId, out Guid parsedProcessId) || parsedProcessId == Guid.Empty)
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "InvalidProviderResponse", "The provider response has no valid process identifier.");
            }

            JsonElement? excelInfo = FindProperty(root, "excel_info");
            bool excelGenerated = BooleanValue(excelInfo is JsonElement excel ? FindProperty(excel, "excel_generated") : null);
            string? excelFileName = excelInfo is JsonElement fileInfo ? ScalarString(FindProperty(fileInfo, "excel_filename")) : null;
            string? excelDownloadUrl = excelInfo is JsonElement urlInfo ? ScalarString(FindProperty(urlInfo, "excel_download_url")) : null;
            if (!excelGenerated || string.IsNullOrWhiteSpace(excelFileName) || string.IsNullOrWhiteSpace(excelDownloadUrl))
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "ExcelNotGenerated", "The provider did not generate a downloadable workbook.");
            }

            if (!TryValidateDownloadUrl(excelFileName, excelDownloadUrl, out string safeFileName))
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "UnsafeDownloadUrl", "The provider returned an unsafe workbook download URL.");
            }

            byte[] excelBytes;
            try
            {
                HashSet<HttpStatusCode> downloadRetryStatuses = [.. TransientStatuses, HttpStatusCode.NotFound];
                using HttpResponseMessage downloadResponse = await SendWithRetryAsync(
                    () => CreateDownloadRequest(excelDownloadUrl),
                    downloadRetryStatuses,
                    cancellationToken).ConfigureAwait(false);
                if (!downloadResponse.IsSuccessStatusCode)
                {
                    return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "ExcelDownloadFailed", "The generated workbook could not be downloaded.");
                }

                excelBytes = await ReadResponseBoundedAsync(downloadResponse, _options.MaxExcelBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException)
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "ExcelTooLarge", "The generated workbook exceeds the configured size limit.");
            }
            catch (HttpRequestException)
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "ExcelDownloadFailed", "The generated workbook could not be downloaded.");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "ExcelDownloadTimeout", "The generated workbook download timed out.");
            }

            string canonicalJson;
            try
            {
                canonicalJson = XlsxCanonicalizer.Canonicalize(excelBytes, request.DocumentCode);
            }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or XmlException)
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "InvalidExcel", "The generated workbook is not a valid supported XLSX file.");
            }

            GeneratedFileArtifactResult excelArtifact = await artifacts.CreatePrivateAsync(
                new GeneratedFileArtifactRequest(
                    OwnerType,
                    request.ExtractionAttemptId,
                    safeFileName,
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    excelBytes),
                request.OwnerUserId.ToString(),
                cancellationToken).ConfigureAwait(false);
            if (excelArtifact.FileAssetStatus != FileAssetStatus.Available)
            {
                return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "ExcelArtifactUnavailable", "The generated workbook artifact was not available.");
            }

            return new ExtractionProviderResult(
                ExtractionProviderOutcome.Succeeded,
                processId,
                "1.0",
                canonicalJson,
                rawArtifact.FileAssetId,
                excelArtifact.FileAssetId,
                metadata,
                null,
                null);
        }
    }

    private async Task<ExtractionProviderResult> ExtractWithTaskApiAsync(
        ExtractionProviderRequest request,
        byte[] sourceBytes,
        string sourceFileName,
        CancellationToken cancellationToken)
    {
        string taskId;
        try
        {
            using HttpResponseMessage submission = await SendWithRetryAsync(
                () => CreateTaskSubmissionRequest(request, sourceBytes, sourceFileName),
                TransientStatuses,
                cancellationToken).ConfigureAwait(false);
            byte[] body = await ReadResponseBoundedAsync(submission, _options.MaxResponseBytes, cancellationToken)
                .ConfigureAwait(false);
            if (!submission.IsSuccessStatusCode)
            {
                return Failed("ProviderHttpError", "The extraction provider rejected the asynchronous task submission.");
            }
            JsonNode root = JsonNode.Parse(body) ?? throw new JsonException("The task submission response is empty.");
            taskId = TryGetString(root, "task_id", out string? value) && Guid.TryParse(value, out Guid parsed) && parsed != Guid.Empty
                ? value!
                : throw new JsonException("The task submission response has no valid task_id.");
        }
        catch (HttpRequestException)
        {
            return Failed("ProviderUnavailable", "The extraction provider could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("ProviderTimeout", "The extraction provider task submission timed out.");
        }
        catch (JsonException)
        {
            return Failed("InvalidProviderResponse", "The extraction provider returned an invalid task submission response.");
        }

        while (true)
        {
            using HttpRequestMessage statusRequest = CreateAuthenticatedRequest(
                HttpMethod.Get,
                $"v1/extraction-tasks/{Uri.EscapeDataString(taskId)}",
                "application/json");
            using HttpResponseMessage statusResponse = await httpClient.SendAsync(
                statusRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            byte[] statusBytes = await ReadResponseBoundedAsync(
                statusResponse,
                _options.MaxResponseBytes,
                cancellationToken).ConfigureAwait(false);
            if (!statusResponse.IsSuccessStatusCode)
            {
                return Failed("ProviderHttpError", "The extraction provider task status request failed.");
            }

            JsonNode statusRoot;
            try
            {
                statusRoot = JsonNode.Parse(statusBytes) ?? throw new JsonException();
            }
            catch (JsonException)
            {
                return Failed("InvalidProviderResponse", "The extraction provider returned an invalid task status response.");
            }
            if (!TryGetString(statusRoot, "task_id", out string? correlatedId)
                || !string.Equals(taskId, correlatedId, StringComparison.Ordinal))
            {
                return Failed("TaskCorrelationMismatch", "The extraction provider task status did not match the submitted task.");
            }
            if (!TryGetString(statusRoot, "status", out string? taskStatus))
            {
                return Failed("InvalidProviderResponse", "The extraction provider task status is missing.");
            }

            string normalizedStatus = taskStatus!.Trim().ToUpperInvariant();
            if (normalizedStatus == "FAILED")
            {
                string? code = statusRoot["error"]?["code"]?.GetValue<string>();
                return Failed(string.IsNullOrWhiteSpace(code) ? "ProviderError" : code, "The extraction provider task failed.");
            }
            if (normalizedStatus == "SUCCEEDED")
            {
                return await CompleteTaskAsync(request, taskId, statusRoot, cancellationToken).ConfigureAwait(false);
            }
            if (normalizedStatus is not ("QUEUED" or "RUNNING"))
            {
                return Failed("InvalidProviderResponse", "The extraction provider returned an unsupported task status.");
            }
            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ExtractionProviderResult> CompleteTaskAsync(
        ExtractionProviderRequest request,
        string taskId,
        JsonNode statusRoot,
        CancellationToken cancellationToken)
    {
        JsonArray? artifactNodes = statusRoot["artifacts"] as JsonArray;
        string? resultUrl = FindArtifactUrl(artifactNodes, "result.json", taskId);
        string? workbookUrl = FindArtifactUrl(artifactNodes, "output.xlsx", taskId);
        if (resultUrl is null || workbookUrl is null)
        {
            return Failed("MissingTaskArtifact", "The extraction provider task did not publish its result and workbook artifacts.");
        }

        byte[] resultBytes = await DownloadTaskArtifactAsync(
            resultUrl,
            "application/json",
            _options.MaxResponseBytes,
            cancellationToken).ConfigureAwait(false);
        byte[] workbookBytes = await DownloadTaskArtifactAsync(
            workbookUrl,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _options.MaxExcelBytes,
            cancellationToken).ConfigureAwait(false);
        JsonNode resultNode;
        try
        {
            resultNode = JsonNode.Parse(resultBytes) ?? throw new JsonException();
        }
        catch (JsonException)
        {
            return Failed("InvalidProviderResponse", "The extraction provider result artifact contains invalid JSON.");
        }

        byte[] sanitized = SanitizeResponse(resultNode);
        GeneratedFileArtifactResult rawArtifact = await artifacts.CreatePrivateAsync(
            new GeneratedFileArtifactRequest(
                OwnerType,
                request.ExtractionAttemptId,
                $"{request.ExtractionAttemptId:N}_aspect-result.json",
                "application/json",
                sanitized),
            request.OwnerUserId.ToString(),
            cancellationToken).ConfigureAwait(false);
        if (rawArtifact.FileAssetStatus != FileAssetStatus.Available)
        {
            return Failed("RawResponseUnavailable", "The extraction provider result artifact was not available.");
        }

        using JsonDocument resultDocument = JsonDocument.Parse(sanitized);
        JsonElement root = resultDocument.RootElement;
        string? processId = ScalarString(FindProperty(root, "process_id")) ?? taskId;
        string? coversheet = ScalarString(FindPropertyRecursive(root, "coversheet_msg"));
        AspectProcessingMetadata metadata = new(coversheet, ReadMetrics(root));
        if (IsNo(ScalarString(FindProperty(root, "Is_Valid"))) || IsYes(coversheet))
        {
            return Invalid(rawArtifact.FileAssetId, processId, metadata, "InvalidDocument", "The provider determined that the document is invalid.");
        }
        if (!IsYes(ScalarString(FindProperty(root, "Is_Valid"))))
        {
            return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "InvalidProviderResponse", "The provider did not confirm document validity.");
        }

        string canonicalJson;
        try
        {
            canonicalJson = XlsxCanonicalizer.Canonicalize(workbookBytes, request.DocumentCode);
        }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException or XmlException)
        {
            return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "InvalidExcel", "The generated workbook is not a valid supported XLSX file.");
        }
        GeneratedFileArtifactResult workbookArtifact = await artifacts.CreatePrivateAsync(
            new GeneratedFileArtifactRequest(
                OwnerType,
                request.ExtractionAttemptId,
                $"{request.ExtractionAttemptId:N}_aspect-output.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                workbookBytes),
            request.OwnerUserId.ToString(),
            cancellationToken).ConfigureAwait(false);
        if (workbookArtifact.FileAssetStatus != FileAssetStatus.Available)
        {
            return FailedWithRaw(rawArtifact.FileAssetId, processId, metadata, "ExcelArtifactUnavailable", "The generated workbook artifact was not available.");
        }

        return new ExtractionProviderResult(
            ExtractionProviderOutcome.Succeeded,
            processId,
            "1.0",
            canonicalJson,
            rawArtifact.FileAssetId,
            workbookArtifact.FileAssetId,
            metadata,
            null,
            null);
    }

    private async Task<byte[]> DownloadTaskArtifactAsync(
        string relativeUrl,
        string accept,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateAuthenticatedRequest(HttpMethod.Get, relativeUrl, accept);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("The extraction task artifact download failed.", null, response.StatusCode);
        return await ReadResponseBoundedAsync(response, maximumBytes, cancellationToken).ConfigureAwait(false);
    }

    private static string? FindArtifactUrl(JsonArray? artifacts, string name, string taskId)
    {
        if (artifacts is null) return null;
        foreach (JsonNode? artifact in artifacts)
        {
            if (!string.Equals(artifact?["name"]?.GetValue<string>(), name, StringComparison.Ordinal)) continue;
            string? url = artifact?["download_url"]?.GetValue<string>();
            string expectedPrefix = $"/v1/extraction-tasks/{Uri.EscapeDataString(taskId)}/artifacts/";
            return url is not null
                && !Uri.TryCreate(url, UriKind.Absolute, out _)
                && url.StartsWith(expectedPrefix, StringComparison.Ordinal)
                && !url.Contains('\\', StringComparison.Ordinal)
                && !url.Contains('?', StringComparison.Ordinal)
                && !url.Contains('#', StringComparison.Ordinal)
                    ? url
                    : null;
        }
        return null;
    }

#pragma warning disable CA2000
    private HttpRequestMessage CreateTaskSubmissionRequest(
        ExtractionProviderRequest request,
        byte[] sourceBytes,
        string sourceFileName)
    {
        ByteArrayContent file = new(sourceBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(sourceFileName));
        MultipartFormDataContent form = new()
        {
            { file, "file", sourceFileName },
            { new StringContent(request.DocumentCode), "doc_type" },
            { new StringContent(bool.TrueString), "generate_excel" },
            { new StringContent(_options.Provider), "provider" },
            { new StringContent(string.IsNullOrWhiteSpace(request.Model) ? _options.Model! : request.Model), "model" }
        };
        HttpRequestMessage message = new(HttpMethod.Post, "v1/extraction-tasks") { Content = form };
        AddHeaders(message);
        return message;
    }
#pragma warning restore CA2000

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url, string accept)
    {
        HttpRequestMessage message = new(method, url);
        message.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        return message;
    }

    private static string ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToUpperInvariant() switch
    {
        ".PDF" => "application/pdf",
        ".DOCX" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".XLSX" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".PPTX" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        _ => "application/octet-stream"
    };

    private static bool TryGetString(JsonNode node, string propertyName, out string? value)
    {
        value = node[propertyName]?.GetValue<string>();
        return !string.IsNullOrWhiteSpace(value);
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> requestFactory,
        HashSet<HttpStatusCode> retryStatuses,
        CancellationToken cancellationToken)
    {
        for (int attempt = 1; attempt <= _options.MaxRetryAttempts; attempt++)
        {
            bool retryAfterException = false;
            try
            {
                using HttpRequestMessage request = requestFactory();
                HttpResponseMessage response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                if (attempt == _options.MaxRetryAttempts || !retryStatuses.Contains(response.StatusCode))
                {
                    return response;
                }

                response.Dispose();
            }
            catch (HttpRequestException) when (attempt < _options.MaxRetryAttempts)
            {
                retryAfterException = true;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < _options.MaxRetryAttempts)
            {
                retryAfterException = true;
            }

            if (retryAfterException || attempt < _options.MaxRetryAttempts)
            {
                double delaySeconds = _options.FirstRetryDelaySeconds * Math.Pow(2, attempt - 1);
                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        throw new InvalidOperationException("The HTTP retry loop completed without a response.");
    }

#pragma warning disable CA2000
    private HttpRequestMessage CreateExtractionRequest(
        ExtractionProviderRequest request,
        byte[] sourceBytes,
        string sourceFileName)
    {
        MultipartFormDataContent multipart = new();
        StreamContent fileContent = new(new MemoryStream(sourceBytes, writable: false));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        multipart.Add(fileContent, "file", sourceFileName);
        multipart.Add(new StringContent(request.DocumentCode, Encoding.UTF8), "doc_type");
        multipart.Add(new StringContent("true", Encoding.UTF8), "generate_excel");
        multipart.Add(new StringContent(_options.Provider, Encoding.UTF8), "provider");
        multipart.Add(new StringContent(string.IsNullOrWhiteSpace(request.Model) ? _options.Model! : request.Model, Encoding.UTF8), "model");

        HttpRequestMessage message = new(HttpMethod.Post, "extract_pdf") { Content = multipart };
        AddHeaders(message);
        return message;
    }
#pragma warning restore CA2000

    private HttpRequestMessage CreateDownloadRequest(string relativeUrl)
    {
        HttpRequestMessage message = new(HttpMethod.Get, relativeUrl);
        message.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        return message;
    }

    private void AddHeaders(HttpRequestMessage message)
    {
        message.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private static async Task<byte[]> ReadResponseBoundedAsync(
        HttpResponseMessage response,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new InvalidDataException("Response exceeds the configured size limit.");
        }

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await ReadBoundedAsync(stream, maxBytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, long maxBytes, CancellationToken cancellationToken)
    {
        using MemoryStream destination = new((int)Math.Min(maxBytes, 81_920));
        byte[] buffer = new byte[81_920];
        long total = 0;
        while (true)
        {
            int read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException("Response exceeds the configured size limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildSourceFileName(Guid attemptId, string originalFileName)
    {
        string baseName = Path.GetFileNameWithoutExtension(originalFileName);
        string safeName = string.Concat(baseName.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '_'));
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "source";
        }

        string extension = Path.GetExtension(originalFileName).ToUpperInvariant() switch
        {
            ".DOCX" => ".docx",
            ".XLSX" => ".xlsx",
            ".PPTX" => ".pptx",
            _ => ".pdf"
        };
        return $"{attemptId:N}_{safeName}{extension}";
    }

    private static bool TryValidateDownloadUrl(string fileName, string downloadUrl, out string safeFileName)
    {
        safeFileName = Path.GetFileName(fileName);
        if (!string.Equals(fileName, safeFileName, StringComparison.Ordinal)
            || fileName.Contains('\\', StringComparison.Ordinal)
            || !fileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
            || fileName is ".xlsx" or "..xlsx"
            || Uri.TryCreate(downloadUrl, UriKind.Absolute, out _)
            || !downloadUrl.StartsWith('/')
            || downloadUrl.Contains('\\', StringComparison.Ordinal)
            || downloadUrl.Contains('?', StringComparison.Ordinal)
            || downloadUrl.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        string expected = "/download_excel/" + Uri.EscapeDataString(fileName);
        return string.Equals(downloadUrl, expected, StringComparison.Ordinal);
    }

    private static byte[] SanitizeResponse(JsonNode response)
    {
        SanitizeNode(response);
        return JsonSerializer.SerializeToUtf8Bytes(response);
    }

    private static void SanitizeNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach ((string key, JsonNode? value) in jsonObject.ToList())
            {
                if (IsSensitiveName(key))
                {
                    jsonObject[key] = "[REDACTED]";
                }
                else if (value is not null)
                {
                    SanitizeNode(value);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (JsonNode value in jsonArray.Where(value => value is not null)!)
            {
                SanitizeNode(value);
            }
        }
    }

    private static Dictionary<string, string> ReadMetrics(JsonElement root)
    {
        JsonElement? summary = FindProperty(root, "processing_summary");
        if (summary is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string> metrics = new(StringComparer.OrdinalIgnoreCase);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (IsSensitiveName(property.Name))
            {
                continue;
            }

            string? value = ScalarString(property.Value);
            if (!string.IsNullOrWhiteSpace(value) && value.Length <= 256)
            {
                metrics[property.Name] = value;
            }
        }

        return metrics;
    }

    private static bool IsSensitiveName(string name) =>
        name.Contains("key", StringComparison.OrdinalIgnoreCase)
        || name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || name.Contains("password", StringComparison.OrdinalIgnoreCase)
        || name.Contains("authorization", StringComparison.OrdinalIgnoreCase)
        || name.Contains("access_token", StringComparison.OrdinalIgnoreCase)
        || name.Contains("refresh_token", StringComparison.OrdinalIgnoreCase);

    private static JsonElement? FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string normalizedName = NormalizeName(name);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (NormalizeName(property.Name) == normalizedName)
            {
                return property.Value;
            }
        }

        return null;
    }

    private static JsonElement? FindPropertyRecursive(JsonElement element, string name)
    {
        JsonElement? direct = FindProperty(element, name);
        if (direct is not null)
        {
            return direct;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                JsonElement? found = FindPropertyRecursive(property.Value, name);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                JsonElement? found = FindPropertyRecursive(item, name);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static string NormalizeName(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static string? ScalarString(JsonElement? value)
    {
        if (value is not JsonElement element)
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    private static bool BooleanValue(JsonElement? value)
    {
        string? scalar = ScalarString(value);
        return string.Equals(scalar, bool.TrueString, StringComparison.OrdinalIgnoreCase)
            || string.Equals(scalar, "yes", StringComparison.OrdinalIgnoreCase)
            || scalar == "1";
    }

    private static bool IsYes(string? value) => string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), bool.TrueString, StringComparison.OrdinalIgnoreCase);

    private static bool IsNo(string? value) => string.Equals(value?.Trim(), "no", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), bool.FalseString, StringComparison.OrdinalIgnoreCase);

    private static ExtractionProviderResult Failed(string code, string message) =>
        new(ExtractionProviderOutcome.Failed, null, null, null, null, null, null, code, message);

    private static ExtractionProviderResult FailedWithRaw(
        Guid rawArtifactId,
        string? processId,
        AspectProcessingMetadata metadata,
        string code,
        string message) =>
        new(ExtractionProviderOutcome.Failed, processId, null, null, rawArtifactId, null, metadata, code, message);

    private static ExtractionProviderResult Invalid(
        Guid rawArtifactId,
        string? processId,
        AspectProcessingMetadata metadata,
        string code,
        string message) =>
        new(ExtractionProviderOutcome.InvalidDocument, processId, null, null, rawArtifactId, null, metadata, code, message);

}
