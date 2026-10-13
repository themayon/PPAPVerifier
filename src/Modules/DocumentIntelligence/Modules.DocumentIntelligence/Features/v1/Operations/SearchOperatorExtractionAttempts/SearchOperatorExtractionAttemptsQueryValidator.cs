using DES.Modules.DocumentIntelligence.Contracts.v1.Operations;
using DES.Modules.DocumentIntelligence.Domain;
using FluentValidation;

namespace DES.Modules.DocumentIntelligence.Features.v1.Operations.SearchOperatorExtractionAttempts;

public sealed class SearchOperatorExtractionAttemptsQueryValidator : AbstractValidator<SearchOperatorExtractionAttemptsQuery>
{
    private static readonly string[] Sorts =
    [
        "createdAt",
        "-createdAt",
        "updatedAt",
        "-updatedAt",
        "status",
        "-status",
        "documentCode",
        "-documentCode",
        "ownerUserId",
        "-ownerUserId"
    ];

    public SearchOperatorExtractionAttemptsQueryValidator()
    {
        RuleFor(x => x.PageNumber).NotNull().InclusiveBetween(1, int.MaxValue);
        RuleFor(x => x.PageSize).NotNull().InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(value => Enum.TryParse<ExtractionAttemptStatus>(value, true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status));
        RuleFor(x => x.DocumentCode).MaximumLength(ExtractionAttempt.DocumentCodeMaxLength);
        RuleFor(x => x.OwnerUserId)
            .NotEqual(Guid.Empty)
            .When(x => x.OwnerUserId.HasValue);
        RuleFor(x => x.Sort)
            .Must(value => Sorts.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Sort));
    }
}
