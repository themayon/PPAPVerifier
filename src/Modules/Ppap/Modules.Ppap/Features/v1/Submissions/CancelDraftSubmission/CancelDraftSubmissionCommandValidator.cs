using DES.Modules.Ppap.Contracts.v1.Submissions;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Submissions.CancelDraftSubmission;

public sealed class CancelDraftSubmissionCommandValidator : AbstractValidator<CancelDraftSubmissionCommand>
{
    public CancelDraftSubmissionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}
