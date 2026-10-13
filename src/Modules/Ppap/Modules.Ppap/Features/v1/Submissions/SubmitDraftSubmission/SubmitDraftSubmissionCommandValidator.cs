using DES.Modules.Ppap.Contracts.v1.Submissions;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Submissions.SubmitDraftSubmission;

public sealed class SubmitDraftSubmissionCommandValidator : AbstractValidator<SubmitDraftSubmissionCommand>
{
    public SubmitDraftSubmissionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}
