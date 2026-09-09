using FluentValidation;
using DES.Modules.Billing.Contracts.v1.Wallets;

namespace DES.Modules.Billing.Features.v1.Wallets.CreateTopupRequest;

public sealed class CreateTopupRequestCommandValidator : AbstractValidator<CreateTopupRequestCommand>
{
    public CreateTopupRequestCommandValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0m).LessThanOrEqualTo(1_000_000m);
        RuleFor(x => x.Note).MaximumLength(512);
    }
}
