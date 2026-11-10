using DES.Modules.Ppap.Contracts.v1.Workspace;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Workspace.DecideFindingWaiverRequest;

public sealed class DecideFindingWaiverRequestCommandValidator : AbstractValidator<DecideFindingWaiverRequestCommand>
{
    public DecideFindingWaiverRequestCommandValidator()
    {
        RuleFor(x => x.RequestId).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(PpapWaiverRequest.ReasonMaxLength);
    }
}
