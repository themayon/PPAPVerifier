using DES.Modules.Ppap.Contracts.v1.Workspace;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Workspace.RequestFindingWaiver;

public sealed class RequestFindingWaiverCommandValidator : AbstractValidator<RequestFindingWaiverCommand>
{
    public RequestFindingWaiverCommandValidator()
    {
        RuleFor(x => x.SubmissionId).NotEmpty();
        RuleFor(x => x.WorkingFindingId).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(PpapWaiverRequest.ReasonMaxLength);
    }
}
