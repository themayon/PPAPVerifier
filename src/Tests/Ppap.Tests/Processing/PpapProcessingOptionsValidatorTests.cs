using DES.Modules.Ppap.Processing;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Processing;

public sealed class PpapProcessingOptionsValidatorTests
{
    [Fact]
    public void Validate_Should_Fail_When_FixtureIsEnabledInProduction()
    {
        PpapProcessingOptionsValidator validator = new(new TestHostEnvironment(Environments.Production));

        ValidateOptionsResult result = validator.Validate(
            null,
            new PpapProcessingOptions { Mode = PpapProcessingMode.Fixture });

        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(241)]
    public void Validate_Should_Fail_When_LeaseMinutesAreOutsideRange(int leaseMinutes)
    {
        PpapProcessingOptionsValidator validator = new(new TestHostEnvironment(Environments.Development));

        ValidateOptionsResult result = validator.Validate(
            null,
            new PpapProcessingOptions { LeaseMinutes = leaseMinutes });

        result.Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(240)]
    public void Validate_Should_Succeed_When_LeaseMinutesAreInsideRange(int leaseMinutes)
    {
        PpapProcessingOptionsValidator validator = new(new TestHostEnvironment(Environments.Development));

        ValidateOptionsResult result = validator.Validate(
            null,
            new PpapProcessingOptions { LeaseMinutes = leaseMinutes });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_Succeed_When_FixtureIsEnabledInDevelopment()
    {
        PpapProcessingOptionsValidator validator = new(new TestHostEnvironment(Environments.Development));

        ValidateOptionsResult result = validator.Validate(
            null,
            new PpapProcessingOptions { Mode = PpapProcessingMode.Fixture });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData(PpapProcessingMode.Disabled)]
    [InlineData(PpapProcessingMode.Real)]
    public void Validate_Should_Succeed_When_NonFixtureModeIsUsedInProduction(PpapProcessingMode mode)
    {
        PpapProcessingOptionsValidator validator = new(new TestHostEnvironment(Environments.Production));

        ValidateOptionsResult result = validator.Validate(
            null,
            new PpapProcessingOptions { Mode = mode });

        result.Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_Fail_When_ModeIsInvalid()
    {
        PpapProcessingOptionsValidator validator = new(new TestHostEnvironment(Environments.Development));

        ValidateOptionsResult result = validator.Validate(
            null,
            new PpapProcessingOptions { Mode = (PpapProcessingMode)99 });

        result.Failed.ShouldBeTrue();
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Ppap.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
