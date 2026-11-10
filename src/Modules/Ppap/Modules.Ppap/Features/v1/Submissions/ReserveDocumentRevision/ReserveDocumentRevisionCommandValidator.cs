using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Submissions.ReserveDocumentRevision;

public sealed class ReserveDocumentRevisionCommandValidator : AbstractValidator<ReserveDocumentRevisionCommand>
{
    public ReserveDocumentRevisionCommandValidator()
    {
        RuleFor(x => x.SubmissionId).NotEmpty();
        RuleFor(x => x.DocumentId).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(PpapDocumentRevision.FileNameMaxLength);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(PpapDocumentRevision.ContentTypeMaxLength);
        RuleFor(x => x.SizeBytes).InclusiveBetween(1, 50L * 1024 * 1024);
    }
}
