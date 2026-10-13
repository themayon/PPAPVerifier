using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Credits.SearchCreditTopupRequests;

public sealed class SearchCreditTopupRequestsQueryValidator : AbstractValidator<SearchCreditTopupRequestsQuery>
{
    private static readonly string[] Sorts =
    [
        "requestedAt",
        "-requestedAt",
        "creditsRequested",
        "-creditsRequested",
        "status",
        "-status",
        "ownerUserId",
        "-ownerUserId"
    ];

    public SearchCreditTopupRequestsQueryValidator()
    {
        RuleFor(x => x.PageNumber).NotNull().InclusiveBetween(1, int.MaxValue);
        RuleFor(x => x.PageSize).NotNull().InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(value => Enum.TryParse<CreditTopupRequestStatus>(value, true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status));
        RuleFor(x => x.Sort)
            .Must(value => Sorts.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Sort));
    }
}
