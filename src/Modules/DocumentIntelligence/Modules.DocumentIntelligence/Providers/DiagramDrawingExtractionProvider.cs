using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DES.Modules.Files.Contracts;
using DES.Modules.Files.Contracts.v1.DTOs;
using Microsoft.Extensions.Options;

namespace DES.Modules.DocumentIntelligence.Providers;

internal sealed class DiagramDrawingExtractionProvider(
    HttpClient httpClient,
    IInternalFileArtifactService artifacts,
    IOptions<DiagramDrawingExtractionOptions> options) : IDocumentExtractionProvider
{
    private const string OwnerType = "DocumentExtraction";
    private readonly DiagramDrawingExtractionOptions _options = options.Value;

    public string ProviderKey => "diagram-drawing";

    public async Task<ExtractionProviderResult> ExtractAsync(
        ExtractionProviderRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_options.Enabled)
        {
            return Failed("ProviderDisabled", "Diagram drawing extraction is disabled.");
        }
        if (request.ExtractionAttemptId == Guid.Empty || request.OwnerUserId == Guid.Empty || request.SourceFileAssetId == Guid.Empty)
        {
            return Failed("InvalidRequest", "Extraction identifiers must be non-empty.");
        }

        SourceReadResult sourceResult = await ReadSourceAsync(request, cancellationToken).ConfigureAwait(false);
        if (sourceResult.Error is not null)
        {
            return sourceResult.Error;
        }
        SourceArtifact source = sourceResult.Source!;

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(_options.TotalTimeoutMinutes));
        try
        {
            string? taskId = await SubmitAsync(request, source, timeout.Token).ConfigureAwait(false);
            if (taskId is null)
            {
                return Failed("InvalidProviderResponse", "The provider submission response has no valid ref_id.");
            }

            return await PollAsync(request, taskId, timeout.Token).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            return Failed("ResponseTooLarge", exception.Message);
        }
        catch (JsonException)
        {
            return Failed("InvalidProviderResponse", "The drawing extraction provider returned invalid JSON.");
        }
        catch (HttpRequestException)
        {
            return Failed("ProviderUnavailable", "The drawing extraction provider could not be reached.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("ProviderTimeout", "The drawing extraction provider did not complete before the configured timeout.");
        }
    }

    private async Task<SourceReadResult> ReadSourceAsync(ExtractionProviderRequest request, CancellationToken cancellationToken)
    {
        await using InternalFileArtifactContent source = await artifacts.OpenReadAsync(
            request.SourceFileAssetId,
            request.OwnerUserId.ToString(),
            cancellationToken).ConfigureAwait(false);
        string extension = Path.GetExtension(source.FileName).ToUpperInvariant();
        string contentType = source.ContentType.ToUpperInvariant();
        bool supported = (extension == ".PDF" && contentType == "APPLICATION/PDF")
            || (extension is ".JPG" or ".JPEG" && contentType == "IMAGE/JPEG")
            || (extension == ".PNG" && contentType == "IMAGE/PNG");
        if (!supported)
        {
            return new SourceReadResult(null, Failed("InvalidSourceType", "The drawing source artifact must be a PDF, JPEG, or PNG."));
        }
        if (source.SizeBytes > _options.MaxSourceBytes)
        {
            return new SourceReadResult(null, Failed("SourceTooLarge", "The drawing source artifact exceeds the configured size limit."));
        }

        byte[] bytes;
        try
        {
            bytes = await ReadBoundedAsync(source.Content, _options.MaxSourceBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return new SourceReadResult(null, Failed("SourceTooLarge", "The drawing source artifact exceeds the configured size limit."));
        }

        string safeExtension = extension switch
        {
            ".PDF" => ".pdf",
            ".PNG" => ".png",
            _ => ".jpg"
        };
        return new SourceReadResult(new SourceArtifact(bytes, source.ContentType, $"{request.ExtractionAttemptId:N}_source{safeExtension}"), null);
    }

    private async Task<string?> SubmitAsync(
        ExtractionProviderRequest request,
        SourceArtifact source,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = CreateSubmissionRequest(request, source);
        using HttpResponseMessage response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        byte[] body = await ReadResponseBoundedAsync(response, _options.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("The drawing provider rejected the submission.", null, response.StatusCode);
        }

        JsonNode root = JsonNode.Parse(body) ?? throw new JsonException("The provider submission response is empty.");
        return TryGetString(root, "ref_id", out string? refId) && IsSafeTaskId(refId) ? refId : null;
    }

    private async Task<ExtractionProviderResult> PollAsync(
        ExtractionProviderRequest request,
        string taskId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            using HttpRequestMessage message = CreateAuthenticatedRequest(HttpMethod.Get, $"omnex-task-status/{Uri.EscapeDataString(taskId)}");
            using HttpResponseMessage response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            byte[] body = await ReadResponseBoundedAsync(response, _options.MaxResponseBytes, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException("The drawing provider status request failed.", null, response.StatusCode);
            }

            JsonNode root = JsonNode.Parse(body) ?? throw new JsonException("The provider status response is empty.");
            if (!TryGetString(root, "task_id", out string? correlatedId)
                || !string.Equals(correlatedId, taskId, StringComparison.Ordinal))
            {
                return Failed("TaskCorrelationMismatch", "The provider status response did not match the submitted task_id.");
            }
            if (!TryGetString(root, "status", out string? status))
            {
                return Failed("InvalidProviderResponse", "The provider status response has no status.");
            }

            string normalizedStatus = status!.Trim().ToUpperInvariant();
            if (normalizedStatus == "COMPLETED")
            {
                return await CompleteAsync(request, taskId, root, cancellationToken).ConfigureAwait(false);
            }
            if (normalizedStatus == "ERROR")
            {
                Guid? rawArtifactId = await SaveRawAsync(request, root, cancellationToken).ConfigureAwait(false);
                return rawArtifactId is null
                    ? Failed("RawResponseUnavailable", "The drawing provider response artifact was not available.")
                    : FailedWithRaw(taskId, rawArtifactId, "ProviderError", "The drawing extraction provider reported an error.");
            }
            if (normalizedStatus is not ("PENDING" or "PROCESSING" or "QUEUED" or "IN_PROGRESS"))
            {
                return Failed("InvalidProviderResponse", "The provider returned an unsupported task status.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<ExtractionProviderResult> CompleteAsync(
        ExtractionProviderRequest request,
        string taskId,
        JsonNode root,
        CancellationToken cancellationToken)
    {
        if (!TryGetNode(root, "result", out JsonNode? resultNode) || resultNode is null)
        {
            return Failed("InvalidProviderResponse", "The completed provider response has no result.");
        }
        JsonNode originalResultNode = resultNode;
        resultNode = ParseEmbeddedJson(resultNode);
        if (!ReferenceEquals(originalResultNode, resultNode) && root is JsonObject rootObject)
        {
            string? resultPropertyName = rootObject.Select(pair => pair.Key)
                .FirstOrDefault(key => NormalizeName(key) == NormalizeName("result"));
            if (resultPropertyName is not null)
            {
                rootObject[resultPropertyName] = resultNode.DeepClone();
            }
        }
        if (resultNode is not JsonObject and not JsonArray)
        {
            return Failed("InvalidProviderResponse", "The completed provider result must be a JSON object or array.");
        }

        string? validity = FindScalarRecursive(resultNode, "Is_Valid");
        Guid? rawArtifactId = await SaveRawAsync(request, root, cancellationToken).ConfigureAwait(false);
        if (rawArtifactId is null)
        {
            return Failed("RawResponseUnavailable", "The drawing provider response artifact was not available.");
        }
        if (IsNo(validity))
        {
            return Invalid(taskId, rawArtifactId, "InvalidDocument", "The provider determined that the drawing is invalid.");
        }
        if (!IsYes(validity))
        {
            return FailedWithRaw(taskId, rawArtifactId, "InvalidProviderResponse", "The provider did not confirm drawing validity.");
        }

        Guid? excelArtifactId = null;
        string? excelUrl = TryGetString(root, "excel_download_url", out string? rootExcelUrl)
            ? rootExcelUrl
            : FindScalarRecursive(resultNode, "excel_download_url");
        if (!string.IsNullOrWhiteSpace(excelUrl))
        {
            if (!TryValidateExcelUrl(excelUrl, taskId, out string safeFileName))
            {
                return FailedWithRaw(taskId, rawArtifactId, "UnsafeDownloadUrl", "The provider returned an unsafe workbook download URL.");
            }
            ExtractionProviderResult? downloadFailure = null;
            (excelArtifactId, downloadFailure) = await DownloadExcelAsync(
                request,
                taskId,
                rawArtifactId,
                excelUrl,
                safeFileName,
                cancellationToken).ConfigureAwait(false);
            if (downloadFailure is not null)
            {
                return downloadFailure;
            }
        }

        JsonNode canonicalResult = SanitizeAndSort(resultNode);
        JsonObject canonical = new()
        {
            ["documentCode"] = "DWG",
            ["provider"] = _options.Provider.Trim(),
            ["model"] = (string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model)!.Trim(),
            ["drawing"] = canonicalResult
        };
        byte[] canonicalBytes = JsonSerializer.SerializeToUtf8Bytes(SortNode(canonical));
        if (canonicalBytes.LongLength > _options.MaxCanonicalJsonBytes)
        {
            return FailedWithRaw(taskId, rawArtifactId, "CanonicalResponseTooLarge", "The canonical drawing exceeds the configured size limit.");
        }

        return new ExtractionProviderResult(
            ExtractionProviderOutcome.Succeeded,
            taskId,
            "drawing-1.0",
            Encoding.UTF8.GetString(canonicalBytes),
            rawArtifactId,
            excelArtifactId,
            null,
            null,
            null);
    }

    private async Task<(Guid? ArtifactId, ExtractionProviderResult? Failure)> DownloadExcelAsync(
        ExtractionProviderRequest request,
        string taskId,
        Guid? rawArtifactId,
        string excelUrl,
        string safeFileName,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage message = CreateAuthenticatedRequest(HttpMethod.Get, excelUrl);
        message.Headers.Accept.Clear();
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
        using HttpResponseMessage response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return (null, FailedWithRaw(taskId, rawArtifactId, "ExcelDownloadFailed", "The drawing workbook could not be downloaded."));
        }

        byte[] excelBytes;
        try
        {
            excelBytes = await ReadResponseBoundedAsync(response, _options.MaxExcelBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            return (null, FailedWithRaw(taskId, rawArtifactId, "ExcelTooLarge", "The drawing workbook exceeds the configured size limit."));
        }

        GeneratedFileArtifactResult artifact = await artifacts.CreatePrivateAsync(
            new GeneratedFileArtifactRequest(
                OwnerType,
                request.ExtractionAttemptId,
                safeFileName,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                excelBytes),
            request.OwnerUserId.ToString(),
            cancellationToken).ConfigureAwait(false);
        if (artifact.FileAssetStatus != FileAssetStatus.Available)
        {
            return (null, FailedWithRaw(taskId, rawArtifactId, "ExcelArtifactUnavailable", "The drawing workbook artifact was not available."));
        }

        return (artifact.FileAssetId, null);
    }

    private async Task<Guid?> SaveRawAsync(
        ExtractionProviderRequest request,
        JsonNode root,
        CancellationToken cancellationToken)
    {
        byte[] sanitized = JsonSerializer.SerializeToUtf8Bytes(SanitizeAndSort(root));
        GeneratedFileArtifactResult artifact = await artifacts.CreatePrivateAsync(
            new GeneratedFileArtifactRequest(
                OwnerType,
                request.ExtractionAttemptId,
                $"{request.ExtractionAttemptId:N}_drawing-response.json",
                "application/json",
                sanitized),
            request.OwnerUserId.ToString(),
            cancellationToken).ConfigureAwait(false);
        return artifact.FileAssetStatus == FileAssetStatus.Available ? artifact.FileAssetId : null;
    }

#pragma warning disable CA2000
    private HttpRequestMessage CreateSubmissionRequest(ExtractionProviderRequest request, SourceArtifact source)
    {
        MultipartFormDataContent multipart = new();
        ByteArrayContent file = new(source.Bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(source.ContentType);
        multipart.Add(file, "file", source.FileName);
        multipart.Add(new StringContent(_options.Provider.Trim(), Encoding.UTF8), "provider");
        multipart.Add(new StringContent((string.IsNullOrWhiteSpace(request.Model) ? _options.Model : request.Model)!.Trim(), Encoding.UTF8), "model");
        multipart.Add(new StringContent("true", Encoding.UTF8), "disable_webhook");
        AddOptional(multipart, "detection_model_api_key", _options.DetectionModelApiKey);
        AddOptional(multipart, "drawing_detection_provider", _options.DrawingDetectionProvider);
        AddOptional(multipart, "drawing_detection_model", _options.DrawingDetectionModel);
        AddOptional(multipart, "drawing_detection_api_key", _options.DrawingDetectionApiKey);
        HttpRequestMessage message = CreateAuthenticatedRequest(HttpMethod.Post, "upload-file");
        AddOptionalHeader(message, "X-Tenant-ID", _options.ExternalTenantId);
        AddOptionalHeader(message, "X-Model-Api-Key", _options.ModelApiKey);
        message.Content = multipart;
        return message;
    }
#pragma warning restore CA2000

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string url)
    {
        HttpRequestMessage message = new(method, url);
        message.Headers.TryAddWithoutValidation("X-API-KEY", _options.ApiKey);
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return message;
    }

    private static void AddOptionalHeader(HttpRequestMessage message, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            message.Headers.TryAddWithoutValidation(name, value.Trim());
        }
    }

#pragma warning disable CA2000
    private static void AddOptional(MultipartFormDataContent multipart, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            multipart.Add(new StringContent(value.Trim(), Encoding.UTF8), name);
        }
    }
#pragma warning restore CA2000

    private static async Task<byte[]> ReadResponseBoundedAsync(
        HttpResponseMessage response,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maxBytes)
        {
            throw new InvalidDataException("The provider response exceeds the configured size limit.");
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
                throw new InvalidDataException("The provider response exceeds the configured size limit.");
            }
            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryValidateExcelUrl(string url, string taskId, out string safeFileName)
    {
        safeFileName = string.Empty;
        if (Uri.TryCreate(url, UriKind.Absolute, out _)
            || !url.StartsWith('/')
            || url.StartsWith("//", StringComparison.Ordinal)
            || url.Contains('\\', StringComparison.Ordinal)
            || url.Contains('?', StringComparison.Ordinal)
            || url.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(url);
        }
        catch (UriFormatException)
        {
            return false;
        }
        string[] segments = decoded.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 2
            || !string.Equals(segments[0], "download-report", StringComparison.Ordinal)
            || !string.Equals(segments[1], taskId, StringComparison.Ordinal))
        {
            return false;
        }
        safeFileName = $"{taskId}_drawing.xlsx";
        return true;
    }

    private static JsonNode ParseEmbeddedJson(JsonNode node)
    {
        if (node is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text))
        {
            return JsonNode.Parse(text) ?? node;
        }
        return node;
    }

    private static JsonNode SanitizeAndSort(JsonNode node)
    {
        JsonNode clone = node.DeepClone();
        SanitizeNode(clone, 0);
        return SortNode(clone);
    }

    private static void SanitizeNode(JsonNode node, int depth)
    {
        if (depth > 32)
        {
            throw new JsonException("The provider response exceeds the supported JSON depth.");
        }
        if (node is JsonObject jsonObject)
        {
            foreach ((string key, JsonNode? value) in jsonObject.ToList())
            {
                if (IsSensitiveOrLocalName(key))
                {
                    jsonObject.Remove(key);
                }
                else if (value is not null)
                {
                    SanitizeNode(value, depth + 1);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            if (jsonArray.Count > 10_000)
            {
                throw new JsonException("The provider response contains too many array elements.");
            }
            foreach (JsonNode value in jsonArray.Where(value => value is not null)!)
            {
                SanitizeNode(value, depth + 1);
            }
        }
    }

    private static JsonNode SortNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            JsonObject sorted = new();
            foreach ((string key, JsonNode? value) in jsonObject.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                sorted[key] = value is null ? null : SortNode(value);
            }
            return sorted;
        }
        if (node is JsonArray jsonArray)
        {
            JsonArray sorted = [];
            foreach (JsonNode? value in jsonArray)
            {
                sorted.Add(value is null ? null : SortNode(value));
            }
            return sorted;
        }
        return node.DeepClone();
    }

    private static bool IsSensitiveOrLocalName(string name)
    {
        string normalized = NormalizeName(name);
        return normalized.EndsWith("KEY", StringComparison.Ordinal)
            || normalized.Contains("APIKEY", StringComparison.Ordinal)
            || normalized.Contains("SECRET", StringComparison.Ordinal)
            || normalized.Contains("PASSWORD", StringComparison.Ordinal)
            || normalized.Contains("TOKEN", StringComparison.Ordinal)
            || normalized.Contains("AUTHORIZATION", StringComparison.Ordinal)
            || normalized.Contains("ACCESSTOKEN", StringComparison.Ordinal)
            || normalized.Contains("REFRESHTOKEN", StringComparison.Ordinal)
            || normalized.Contains("BASE64", StringComparison.Ordinal)
            || normalized.Contains("BLOB", StringComparison.Ordinal)
            || normalized.Contains("LOCALPATH", StringComparison.Ordinal)
            || normalized.EndsWith("PATH", StringComparison.Ordinal)
            || normalized.EndsWith("DIRECTORY", StringComparison.Ordinal)
            || normalized.EndsWith("DIR", StringComparison.Ordinal)
            || normalized.Contains("DOWNLOADURL", StringComparison.Ordinal)
            || normalized is "IMAGE" or "IMAGES" or "IMAGEBYTES" or "IMAGECONTENT";
    }

    private static bool TryGetString(JsonNode node, string name, out string? value)
    {
        value = null;
        if (!TryGetNode(node, name, out JsonNode? found) || found is not JsonValue scalar || !scalar.TryGetValue(out string? text))
        {
            return false;
        }
        value = text;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetNode(JsonNode node, string name, out JsonNode? value)
    {
        value = null;
        if (node is not JsonObject jsonObject)
        {
            return false;
        }
        string normalized = NormalizeName(name);
        KeyValuePair<string, JsonNode?> found = jsonObject.FirstOrDefault(pair => NormalizeName(pair.Key) == normalized);
        if (found.Key is null)
        {
            return false;
        }
        value = found.Value;
        return true;
    }

    private static string? FindScalarRecursive(JsonNode node, string name)
    {
        if (TryGetString(node, name, out string? value))
        {
            return value;
        }
        if (node is JsonObject jsonObject)
        {
            foreach (JsonNode? child in jsonObject.Select(pair => pair.Value))
            {
                if (child is not null && FindScalarRecursive(child, name) is { } found)
                {
                    return found;
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (JsonNode? child in jsonArray)
            {
                if (child is not null && FindScalarRecursive(child, name) is { } found)
                {
                    return found;
                }
            }
        }
        return null;
    }

    private static string NormalizeName(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();

    private static bool IsSafeTaskId(string? taskId) => taskId is { Length: > 0 and <= 100 }
        && taskId.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static bool IsYes(string? value) => string.Equals(value?.Trim(), "yes", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), bool.TrueString, StringComparison.OrdinalIgnoreCase);

    private static bool IsNo(string? value) => string.Equals(value?.Trim(), "no", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value?.Trim(), bool.FalseString, StringComparison.OrdinalIgnoreCase);

    private static ExtractionProviderResult Failed(string code, string message) =>
        new(ExtractionProviderOutcome.Failed, null, null, null, null, null, null, code, message);

    private static ExtractionProviderResult FailedWithRaw(
        string taskId,
        Guid? rawArtifactId,
        string code,
        string message) =>
        new(ExtractionProviderOutcome.Failed, taskId, null, null, rawArtifactId, null, null, code, message);

    private static ExtractionProviderResult Invalid(
        string taskId,
        Guid? rawArtifactId,
        string code,
        string message) =>
        new(ExtractionProviderOutcome.InvalidDocument, taskId, null, null, rawArtifactId, null, null, code, message);

    private sealed record SourceArtifact(byte[] Bytes, string ContentType, string FileName);
    private sealed record SourceReadResult(SourceArtifact? Source, ExtractionProviderResult? Error);
}
