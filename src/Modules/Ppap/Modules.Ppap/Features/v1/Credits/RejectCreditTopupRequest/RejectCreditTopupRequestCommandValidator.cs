using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Credits.RejectCreditTopupRequest;

public sealed class RejectCreditTopupRequestCommandValidator : AbstractValidator<RejectCreditTopupRequestCommand>
{
    public RejectCreditTopupRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DecisionNote).MaximumLength(CreditTopupRequest.MaxDecisionNoteLength);
    }
}
