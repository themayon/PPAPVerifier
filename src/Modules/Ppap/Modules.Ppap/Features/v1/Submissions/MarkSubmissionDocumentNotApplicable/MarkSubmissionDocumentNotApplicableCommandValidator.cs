using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Submissions.MarkSubmissionDocumentNotApplicable;

public sealed class MarkSubmissionDocumentNotApplicableCommandValidator
    : AbstractValidator<MarkSubmissionDocumentNotApplicableCommand>
{
    public MarkSubmissionDocumentNotApplicableCommandValidator()
    {
        RuleFor(x => x.SubmissionId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.Reason)
            .NotEmpty()
            .MaximumLength(PpapSubmissionDocument.NotApplicableReasonMaxLength);
    }
}
