using DES.Modules.DocumentIntelligence.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Providers;

public sealed class AspectPdfExtractionOptionsTests
{
    [Fact]
    public void Defaults_Should_UseDurableProcessingValues()
    {
        AspectPdfExtractionOptions options = new();

        options.ExtractorVersion.ShouldBe("aspect-v1");
        options.LeaseMinutes.ShouldBe(120);
        options.MaxAttempts.ShouldBe(3);
    }

    [Fact]
    public void Validate_Should_Fail_When_EnabledWithoutApiKey()
    {
        AspectPdfExtractionOptions options = ValidOptions();
        options.ApiKey = null;
        AspectPdfExtractionOptionsValidator validator = CreateValidator(Environments.Development);

        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("ApiKey");
    }

    [Fact]
    public void Validate_Should_Fail_When_ProductionUrlUsesHttp()
    {
        AspectPdfExtractionOptions options = ValidOptions();
        options.BaseUrl = "http://aspect.test";
        AspectPdfExtractionOptionsValidator validator = CreateValidator(Environments.Production);

        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("HTTPS");
    }

    [Fact]
    public void Validate_Should_AllowHttp_When_DevelopmentIsEnabled()
    {
        AspectPdfExtractionOptions options = ValidOptions();
        options.BaseUrl = "http://localhost:8000";
        AspectPdfExtractionOptionsValidator validator = CreateValidator(Environments.Development);

        ValidateOptionsResult result = validator.Validate(null, options);

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_ValidateRanges_When_Disabled()
    {
        AspectPdfExtractionOptions options = new() { Enabled = false, MaxRetryAttempts = 4 };
        AspectPdfExtractionOptionsValidator validator = CreateValidator(Environments.Development);

        ValidateOptionsResult result = validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("MaxRetryAttempts");
    }

    [Fact]
    public void Validate_Should_Fail_When_LeaseIsShorterThanTimeoutOrAttemptsInvalid()
    {
        AspectPdfExtractionOptions options = ValidOptions();
        options.TimeoutMinutes = 100;
        options.LeaseMinutes = 99;
        options.MaxAttempts = 0;

        ValidateOptionsResult result = CreateValidator(Environments.Development).Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("LeaseMinutes");
        result.FailureMessage.ShouldContain("MaxAttempts");
    }

    private static AspectPdfExtractionOptionsValidator CreateValidator(string environmentName)
    {
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);
        return new AspectPdfExtractionOptionsValidator(environment);
    }

    private static AspectPdfExtractionOptions ValidOptions() => new()
    {
        Enabled = true,
        BaseUrl = "https://aspect.test",
        ApiKey = "real-test-key",
        Provider = "gemini",
        Model = "gemini-model"
    };
}
