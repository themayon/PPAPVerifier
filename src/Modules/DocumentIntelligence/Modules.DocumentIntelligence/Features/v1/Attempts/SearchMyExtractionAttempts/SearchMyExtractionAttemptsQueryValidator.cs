using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using DES.Modules.DocumentIntelligence.Domain;
using FluentValidation;

namespace DES.Modules.DocumentIntelligence.Features.v1.Attempts.SearchMyExtractionAttempts;

public sealed class SearchMyExtractionAttemptsQueryValidator : AbstractValidator<SearchMyExtractionAttemptsQuery>
{
    private static readonly string[] Sorts =
    [
        "createdAt",
        "-createdAt",
        "updatedAt",
        "-updatedAt",
        "status",
        "-status"
    ];

    public SearchMyExtractionAttemptsQueryValidator()
    {
        RuleFor(x => x.PageNumber).NotNull().InclusiveBetween(1, int.MaxValue);
        RuleFor(x => x.PageSize).NotNull().InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(value => Enum.TryParse<ExtractionAttemptStatus>(value, true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status));
        RuleFor(x => x.Sort)
            .Must(value => Sorts.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Sort));
    }
}
