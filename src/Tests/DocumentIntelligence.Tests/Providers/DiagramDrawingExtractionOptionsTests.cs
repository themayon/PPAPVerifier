using DES.Modules.DocumentIntelligence.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Providers;

public sealed class DiagramDrawingExtractionOptionsTests
{
    [Fact]
    public void Defaults_Should_BeDisabledAndLeaseCompatible()
    {
        DiagramDrawingExtractionOptions options = new();

        options.Enabled.ShouldBeFalse();
        options.PollIntervalSeconds.ShouldBe(2);
        ((double)options.LeaseMinutes).ShouldBeGreaterThan(options.TotalTimeoutMinutes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("replace-with-real-key")]
    [InlineData("your-api-key")]
    public void Validate_Should_RejectMissingOrPlaceholderApiKey_When_Enabled(string? apiKey)
    {
        DiagramDrawingExtractionOptions options = ValidOptions();
        options.ApiKey = apiKey;

        ValidateOptionsResult result = CreateValidator(Environments.Development).Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ApiKey");
    }

    [Fact]
    public void Validate_Should_RequireHttps_When_Production()
    {
        DiagramDrawingExtractionOptions options = ValidOptions();
        options.BaseUrl = "http://diagram.test";

        ValidateOptionsResult result = CreateValidator(Environments.Production).Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("HTTPS");
    }

    [Fact]
    public void Validate_Should_RejectTimeoutThatExceedsLease()
    {
        DiagramDrawingExtractionOptions options = ValidOptions();
        options.TotalTimeoutMinutes = 20;
        options.LeaseMinutes = 20;
        options.PollIntervalSeconds = 1;

        ValidateOptionsResult result = CreateValidator(Environments.Development).Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("LeaseMinutes");
    }

    [Fact]
    public void Validate_Should_ValidateBoundsAndOptionalSecrets_When_Disabled()
    {
        DiagramDrawingExtractionOptions options = new()
        {
            MaxResponseBytes = 0,
            ModelApiKey = "replace-with-model-key",
            DetectionModelApiKey = "changeme",
            DrawingDetectionProvider = new string('p', 101),
            DrawingDetectionModel = new string('m', 201),
            DrawingDetectionApiKey = new string('k', 1001)
        };

        ValidateOptionsResult result = CreateValidator(Environments.Development).Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MaxResponseBytes");
        result.FailureMessage.ShouldContain("ModelApiKey");
        result.FailureMessage.ShouldContain("DetectionModelApiKey");
        result.FailureMessage.ShouldContain("DrawingDetectionProvider");
        result.FailureMessage.ShouldContain("DrawingDetectionModel");
        result.FailureMessage.ShouldContain("DrawingDetectionApiKey");
    }

    private static DiagramDrawingExtractionOptionsValidator CreateValidator(string environmentName)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return new DiagramDrawingExtractionOptionsValidator(environment);
    }

    private static DiagramDrawingExtractionOptions ValidOptions() => new()
    {
        Enabled = true,
        BaseUrl = "https://diagram.test/api/",
        ApiKey = "real-test-key",
        Provider = "gemini",
        Model = "drawing-model",
        PollIntervalSeconds = 0.001,
        TotalTimeoutMinutes = 1,
        LeaseMinutes = 2
    };
}
