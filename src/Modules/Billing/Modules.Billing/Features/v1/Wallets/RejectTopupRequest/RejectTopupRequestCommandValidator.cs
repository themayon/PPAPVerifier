using FluentValidation;
using DES.Modules.Billing.Contracts.v1.Wallets;

namespace DES.Modules.Billing.Features.v1.Wallets.RejectTopupRequest;

public sealed class RejectTopupRequestCommandValidator : AbstractValidator<RejectTopupRequestCommand>
{
    public RejectTopupRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Reason).MaximumLength(512);
    }
}
