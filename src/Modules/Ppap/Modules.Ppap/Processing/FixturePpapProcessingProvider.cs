using Microsoft.Extensions.Options;

namespace DES.Modules.Ppap.Processing;

public sealed class FixturePpapProcessingProvider(IOptions<PpapProcessingOptions> options)
    : IPpapProcessingProvider
{
    public Task<PpapProcessingResult> ProcessAsync(
        PpapProcessingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        PpapProcessingResult result = options.Value.Mode != PpapProcessingMode.Fixture
            ? PpapProcessingResult.Failure(
                "PROVIDER_DISABLED",
                "PPAP processing is not enabled.")
            : options.Value.FixtureOutcome switch
            {
                FixturePpapOutcome.Success => PpapProcessingResult.Success(),
                FixturePpapOutcome.Failure => PpapProcessingResult.Failure(
                    "FIXTURE_FAILURE",
                    "Fixture PPAP processing failed."),
                _ => PpapProcessingResult.Failure(
                    "FIXTURE_OUTCOME_INVALID",
                    "Fixture PPAP processing outcome is invalid.")
            };

        return Task.FromResult(result);
    }
}
