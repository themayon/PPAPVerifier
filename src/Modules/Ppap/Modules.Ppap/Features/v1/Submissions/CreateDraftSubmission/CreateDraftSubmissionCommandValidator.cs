using DES.Modules.Ppap.Contracts.v1.Submissions;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Submissions.CreateDraftSubmission;

public sealed class CreateDraftSubmissionCommandValidator : AbstractValidator<CreateDraftSubmissionCommand>
{
    public CreateDraftSubmissionCommandValidator()
    {
        RuleFor(x => x.PartName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PartNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DrawingRevision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PpapLevel).InclusiveBetween(1, 5);
        RuleFor(x => x.RuleTemplateId)
            .Must(id => id != Guid.Empty)
            .When(x => x.RuleTemplateId.HasValue)
            .WithMessage("Rule template ID must not be empty.");
    }
}
