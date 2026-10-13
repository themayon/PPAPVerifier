using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Credits.CreateCreditTopupRequest;

public sealed class CreateCreditTopupRequestCommandValidator : AbstractValidator<CreateCreditTopupRequestCommand>
{
    public CreateCreditTopupRequestCommandValidator()
    {
        RuleFor(x => x.CreditsRequested).Must(x => x is 10 or 50 or 100);
        RuleFor(x => x.PaymentReference).MaximumLength(CreditTopupRequest.MaxPaymentReferenceLength);
        RuleFor(x => x.Note).MaximumLength(CreditTopupRequest.MaxNoteLength);
    }
}
