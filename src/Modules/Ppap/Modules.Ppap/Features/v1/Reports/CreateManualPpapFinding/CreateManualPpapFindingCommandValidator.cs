using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Reports.CreateManualPpapFinding;

public sealed class CreateManualPpapFindingCommandValidator : AbstractValidator<CreateManualPpapFindingCommand>
{
    public CreateManualPpapFindingCommandValidator()
    {
        RuleFor(x => x.ReportId).NotEmpty();
        RuleFor(x => x.Message).NotEmpty().MaximumLength(PpapManualFinding.MessageMaxLength);
        RuleFor(x => x.EvidenceNote).MaximumLength(PpapManualFinding.EvidenceNoteMaxLength);
        RuleFor(x => x).Must(x =>
            (x.SubmissionDocumentId is null && x.CanonicalDocumentId is null && x.ExtractionAttemptId is null)
            || (x.SubmissionDocumentId is not null && x.SubmissionDocumentId != Guid.Empty
                && x.CanonicalDocumentId is not null && x.CanonicalDocumentId != Guid.Empty
                && x.ExtractionAttemptId is not null && x.ExtractionAttemptId != Guid.Empty))
            .WithMessage("Document correlation must provide non-empty submission document, canonical document, and extraction attempt IDs together.");
    }
}
