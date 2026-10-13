using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DES.Modules.DocumentIntelligence.Providers;

public sealed class DiagramDrawingExtractionOptions
{
    internal const long DefaultMaxSourceBytes = 25L * 1024 * 1024;
    internal const long DefaultMaxResponseBytes = 4L * 1024 * 1024;
    internal const long DefaultMaxExcelBytes = 25L * 1024 * 1024;
    internal const long DefaultMaxCanonicalJsonBytes = 2L * 1024 * 1024;

    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string Provider { get; set; } = "gemini";
    public string? Model { get; set; }
    public string ExtractorVersion { get; set; } = "diagram-drawing-v1";
    public double PollIntervalSeconds { get; set; } = 2;
    public double TotalTimeoutMinutes { get; set; } = 100;
    public int LeaseMinutes { get; set; } = 120;
    public long MaxSourceBytes { get; set; } = DefaultMaxSourceBytes;
    public long MaxResponseBytes { get; set; } = DefaultMaxResponseBytes;
    public long MaxExcelBytes { get; set; } = DefaultMaxExcelBytes;
    public long MaxCanonicalJsonBytes { get; set; } = DefaultMaxCanonicalJsonBytes;
    public string? ExternalTenantId { get; set; }
    public string? ModelApiKey { get; set; }
    public string? DetectionModelApiKey { get; set; }
    public string? DrawingDetectionProvider { get; set; }
    public string? DrawingDetectionModel { get; set; }
    public string? DrawingDetectionApiKey { get; set; }
}

internal sealed class DiagramDrawingExtractionOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<DiagramDrawingExtractionOptions>
{
    public ValidateOptionsResult Validate(string? name, DiagramDrawingExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];

        AddFailure(!double.IsFinite(options.PollIntervalSeconds) || options.PollIntervalSeconds is < 0.001 or > 300, "PollIntervalSeconds must be between 0.001 and 300.", failures);
        AddFailure(!double.IsFinite(options.TotalTimeoutMinutes) || options.TotalTimeoutMinutes is < 0.01 or > 240, "TotalTimeoutMinutes must be between 0.01 and 240.", failures);
        AddFailure(options.LeaseMinutes is < 1 or > 240, "LeaseMinutes must be between 1 and 240.", failures);
        double requiredLeaseMinutes = options.TotalTimeoutMinutes + (options.PollIntervalSeconds / 60);
        AddFailure(options.LeaseMinutes < requiredLeaseMinutes, "LeaseMinutes must cover TotalTimeoutMinutes and one polling interval.", failures);
        AddFailure(options.MaxSourceBytes is < 1 or > 104_857_600, "MaxSourceBytes must be between 1 and 104857600.", failures);
        AddFailure(options.MaxResponseBytes is < 1 or > 26_214_400, "MaxResponseBytes must be between 1 and 26214400.", failures);
        AddFailure(options.MaxExcelBytes is < 1 or > 104_857_600, "MaxExcelBytes must be between 1 and 104857600.", failures);
        AddFailure(options.MaxCanonicalJsonBytes is < 1 or > 26_214_400, "MaxCanonicalJsonBytes must be between 1 and 26214400.", failures);
        AddLengthFailure(options.ExtractorVersion, 100, "ExtractorVersion", required: true, failures);
        AddLengthFailure(options.ExternalTenantId, 200, "ExternalTenantId", required: false, failures);
        AddOptionalValueFailure(options.DrawingDetectionProvider, 100, "DrawingDetectionProvider", failures);
        AddOptionalValueFailure(options.DrawingDetectionModel, 200, "DrawingDetectionModel", failures);
        AddOptionalSecretFailure(options.ModelApiKey, "ModelApiKey", failures);
        AddOptionalSecretFailure(options.DetectionModelApiKey, "DetectionModelApiKey", failures);
        AddOptionalSecretFailure(options.DrawingDetectionApiKey, "DrawingDetectionApiKey", failures);

        if (options.Enabled)
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out Uri? baseUri)
                || (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add("BaseUrl must be an absolute HTTP or HTTPS URL when enabled.");
            }
            else if (environment.IsProduction() && baseUri.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add("BaseUrl must use HTTPS in production when enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Trim().Length > 1000 || IsPlaceholder(options.ApiKey))
            {
                failures.Add("ApiKey must contain at most 1000 characters and be configured with a non-placeholder value when enabled.");
            }

            AddLengthFailure(options.Provider, 100, "Provider", required: true, failures);
            AddLengthFailure(options.Model, 200, "Model", required: true, failures);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddLengthFailure(string? value, int maximumLength, string propertyName, bool required, List<string> failures)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value?.Trim().Length > maximumLength)
        {
            failures.Add($"{propertyName} must contain between {(required ? 1 : 0)} and {maximumLength} characters.");
        }
    }

    private static void AddOptionalValueFailure(string? value, int maximumLength, string propertyName, List<string> failures)
    {
        if (!string.IsNullOrWhiteSpace(value) && (value.Trim().Length > maximumLength || IsPlaceholder(value)))
        {
            failures.Add($"{propertyName} must contain at most {maximumLength} characters and must not be a placeholder when configured.");
        }
    }

    private static void AddOptionalSecretFailure(string? value, string propertyName, List<string> failures)
    {
        AddOptionalValueFailure(value, 1000, propertyName, failures);
    }

    private static bool IsPlaceholder(string value) =>
        value.Contains("replace-with", StringComparison.OrdinalIgnoreCase)
        || value.Contains("changeme", StringComparison.OrdinalIgnoreCase)
        || value.Contains("your-api-key", StringComparison.OrdinalIgnoreCase)
        || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

    private static void AddFailure(bool failed, string message, List<string> failures)
    {
        if (failed)
        {
            failures.Add(message);
        }
    }
}
