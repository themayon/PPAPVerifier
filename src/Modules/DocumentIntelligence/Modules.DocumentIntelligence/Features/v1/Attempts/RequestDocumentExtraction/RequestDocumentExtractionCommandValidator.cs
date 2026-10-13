using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using DES.Modules.DocumentIntelligence.Domain;
using FluentValidation;

namespace DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;

public sealed class RequestDocumentExtractionCommandValidator : AbstractValidator<RequestDocumentExtractionCommand>
{
    public RequestDocumentExtractionCommandValidator()
    {
        RuleFor(x => x.OwnerUserId).NotEmpty();
        RuleFor(x => x.SubmissionDocumentId).NotEmpty();
        RuleFor(x => x.SourceFileAssetId).NotEmpty();
        RuleFor(x => x.DocumentCode).NotEmpty().MaximumLength(ExtractionAttempt.DocumentCodeMaxLength);
    }
}
