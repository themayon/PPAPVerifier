using DES.Modules.Ppap.Processing;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Processing;

public sealed class FixturePpapProcessingProviderTests
{
    [Fact]
    public async Task ProcessAsync_Should_ReturnSuccess_When_EnabledForSuccess()
    {
        FixturePpapProcessingProvider provider = CreateProvider(PpapProcessingMode.Fixture, FixturePpapOutcome.Success);

        PpapProcessingResult result = await provider.ProcessAsync(CreateRequest(), CancellationToken.None);

        result.Succeeded.ShouldBeTrue();
        result.ErrorCode.ShouldBeNull();
        result.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task ProcessAsync_Should_ReturnDeterministicFailure_When_EnabledForFailure()
    {
        FixturePpapProcessingProvider provider = CreateProvider(PpapProcessingMode.Fixture, FixturePpapOutcome.Failure);

        PpapProcessingResult first = await provider.ProcessAsync(CreateRequest(), CancellationToken.None);
        PpapProcessingResult second = await provider.ProcessAsync(CreateRequest(), CancellationToken.None);

        first.ShouldBe(second);
        first.Succeeded.ShouldBeFalse();
        first.ErrorCode.ShouldBe("FIXTURE_FAILURE");
        first.ErrorMessage.ShouldBe("Fixture PPAP processing failed.");
    }

    [Fact]
    public async Task ProcessAsync_Should_FailClosed_When_Disabled()
    {
        FixturePpapProcessingProvider provider = CreateProvider(PpapProcessingMode.Disabled, FixturePpapOutcome.Success);

        PpapProcessingResult result = await provider.ProcessAsync(CreateRequest(), CancellationToken.None);

        result.Succeeded.ShouldBeFalse();
        result.ErrorCode.ShouldBe("PROVIDER_DISABLED");
    }

    private static FixturePpapProcessingProvider CreateProvider(
        PpapProcessingMode mode,
        FixturePpapOutcome outcome) =>
        new(Options.Create(new PpapProcessingOptions
        {
            Mode = mode,
            FixtureOutcome = outcome
        }));

    private static PpapProcessingRequest CreateRequest() =>
        new("tenant-1", Guid.NewGuid(), Guid.NewGuid(), 2, Guid.NewGuid());
}
