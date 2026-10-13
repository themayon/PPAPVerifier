using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Submissions.AddSubmissionDocument;

public sealed class AddSubmissionDocumentCommandValidator : AbstractValidator<AddSubmissionDocumentCommand>
{
    public AddSubmissionDocumentCommandValidator()
    {
        RuleFor(x => x.SubmissionId).NotEmpty();
        RuleFor(x => x.DocumentCode)
            .NotEmpty()
            .Must(code => code is not null && PpapDocumentCodes.All.Contains(code.Trim()))
            .WithMessage("DocumentCode must be a supported PPAP document code.");
    }
}
