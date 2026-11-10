using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DES.Modules.DocumentIntelligence.Providers;

public sealed class AspectPdfExtractionOptions
{
    internal const long DefaultMaxSourceBytes = 25L * 1024 * 1024;
    internal const long DefaultMaxResponseBytes = 2L * 1024 * 1024;
    internal const long DefaultMaxExcelBytes = 25L * 1024 * 1024;

    public bool Enabled { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string Provider { get; set; } = "gemini";
    public string? Model { get; set; }
    public string ExtractorVersion { get; set; } = "aspect-v1";
    public int TimeoutMinutes { get; set; } = 100;
    public int LeaseMinutes { get; set; } = 120;
    public int MaxAttempts { get; set; } = 3;
    public long MaxSourceBytes { get; set; } = DefaultMaxSourceBytes;
    public long MaxResponseBytes { get; set; } = DefaultMaxResponseBytes;
    public long MaxExcelBytes { get; set; } = DefaultMaxExcelBytes;
    public int MaxRetryAttempts { get; set; } = 3;
    public double FirstRetryDelaySeconds { get; set; } = 1;
    public bool UseAsyncTaskApi { get; set; }
    public double PollIntervalSeconds { get; set; } = 2;
}

internal sealed class AspectPdfExtractionOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<AspectPdfExtractionOptions>
{
    public ValidateOptionsResult Validate(string? name, AspectPdfExtractionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        List<string> failures = [];

        AddRangeFailure(options.TimeoutMinutes is < 1 or > 240, "TimeoutMinutes must be between 1 and 240.", failures);
        AddRangeFailure(options.LeaseMinutes is < 1 or > 240, "LeaseMinutes must be between 1 and 240.", failures);
        AddRangeFailure(options.LeaseMinutes < options.TimeoutMinutes, "LeaseMinutes must be greater than or equal to TimeoutMinutes.", failures);
        AddRangeFailure(options.MaxAttempts is < 1 or > 10, "MaxAttempts must be between 1 and 10.", failures);
        AddRangeFailure(string.IsNullOrWhiteSpace(options.ExtractorVersion) || options.ExtractorVersion.Trim().Length > 100, "ExtractorVersion must contain between 1 and 100 characters.", failures);
        AddRangeFailure(options.MaxSourceBytes is < 1 or > 104_857_600, "MaxSourceBytes must be between 1 and 104857600.", failures);
        AddRangeFailure(options.MaxResponseBytes is < 1 or > 26_214_400, "MaxResponseBytes must be between 1 and 26214400.", failures);
        AddRangeFailure(options.MaxExcelBytes is < 1 or > 104_857_600, "MaxExcelBytes must be between 1 and 104857600.", failures);
        AddRangeFailure(options.MaxRetryAttempts is < 1 or > 3, "MaxRetryAttempts must be between 1 and 3.", failures);
        AddRangeFailure(options.FirstRetryDelaySeconds is < 0 or > 300, "FirstRetryDelaySeconds must be between 0 and 300.", failures);
        AddRangeFailure(!double.IsFinite(options.PollIntervalSeconds) || options.PollIntervalSeconds is < 0.1 or > 300, "PollIntervalSeconds must be between 0.1 and 300.", failures);

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

            if (string.IsNullOrWhiteSpace(options.ApiKey) || IsPlaceholder(options.ApiKey))
            {
                failures.Add("ApiKey must be configured with a non-placeholder value when enabled.");
            }

            if (!string.Equals(options.Provider, "openai", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(options.Provider, "gemini", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("Provider must be either openai or gemini when enabled.");
            }

            if (string.IsNullOrWhiteSpace(options.Model))
            {
                failures.Add("Model must be configured when enabled.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsPlaceholder(string value) =>
        value.Contains("replace-with", StringComparison.OrdinalIgnoreCase)
        || value.Contains("changeme", StringComparison.OrdinalIgnoreCase)
        || value.Contains("your-api-key", StringComparison.OrdinalIgnoreCase)
        || value.Contains("placeholder", StringComparison.OrdinalIgnoreCase);

    private static void AddRangeFailure(bool failed, string message, List<string> failures)
    {
        if (failed)
        {
            failures.Add(message);
        }
    }
}
