using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Credits.ApproveCreditTopupRequest;

public sealed class ApproveCreditTopupRequestCommandValidator : AbstractValidator<ApproveCreditTopupRequestCommand>
{
    public ApproveCreditTopupRequestCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DecisionNote).MaximumLength(CreditTopupRequest.MaxDecisionNoteLength);
    }
}
