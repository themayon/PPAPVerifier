using DES.Modules.Ppap.Contracts.v1.Submissions;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Submissions.UpdateDraftSubmission;

public sealed class UpdateDraftSubmissionCommandValidator : AbstractValidator<UpdateDraftSubmissionCommand>
{
    public UpdateDraftSubmissionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PartName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PartNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DrawingRevision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PpapLevel).InclusiveBetween(1, 5);
    }
}
