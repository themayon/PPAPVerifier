using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Credits.SearchMyCreditTopupRequests;

public sealed class SearchMyCreditTopupRequestsQueryValidator : AbstractValidator<SearchMyCreditTopupRequestsQuery>
{
    public SearchMyCreditTopupRequestsQueryValidator()
    {
        RuleFor(x => x.PageNumber).NotNull().InclusiveBetween(1, int.MaxValue);
        RuleFor(x => x.PageSize).NotNull().InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(value => Enum.TryParse<CreditTopupRequestStatus>(value, true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status));
    }
}
