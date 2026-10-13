using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DES.Modules.Ppap.Processing;

public sealed class PpapProcessingOptions
{
    public PpapProcessingMode Mode { get; set; } = PpapProcessingMode.Disabled;
    public FixturePpapOutcome FixtureOutcome { get; set; } = FixturePpapOutcome.Failure;
    public int LeaseMinutes { get; set; } = 30;
}

public enum PpapProcessingMode
{
    Disabled = 0,
    Fixture = 1,
    Real = 2
}

public enum FixturePpapOutcome
{
    Success = 0,
    Failure = 1
}

public sealed class PpapProcessingOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<PpapProcessingOptions>
{
    public ValidateOptionsResult Validate(string? name, PpapProcessingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.LeaseMinutes is < 1 or > 240)
        {
            return ValidateOptionsResult.Fail("PPAP processing lease minutes must be between 1 and 240.");
        }
        if (!Enum.IsDefined(options.Mode))
        {
            return ValidateOptionsResult.Fail("The PPAP processing mode is invalid.");
        }

        return environment.IsProduction() && options.Mode == PpapProcessingMode.Fixture
            ? ValidateOptionsResult.Fail("The fixture PPAP processing mode cannot be used in production.")
            : ValidateOptionsResult.Success;
    }
}
