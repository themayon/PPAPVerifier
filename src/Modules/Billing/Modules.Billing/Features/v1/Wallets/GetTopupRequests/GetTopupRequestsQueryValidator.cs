using FluentValidation;
using DES.Modules.Billing.Contracts.v1.Wallets;

namespace DES.Modules.Billing.Features.v1.Wallets.GetTopupRequests;

public sealed class GetTopupRequestsQueryValidator : AbstractValidator<GetTopupRequestsQuery>
{
    public GetTopupRequestsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
