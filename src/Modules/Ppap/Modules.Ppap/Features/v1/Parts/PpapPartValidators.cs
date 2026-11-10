using DES.Modules.Ppap.Contracts.v1.Parts;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Parts;

public sealed class SearchMyPpapPartsQueryValidator : AbstractValidator<SearchMyPpapPartsQuery>
{
    public SearchMyPpapPartsQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Sort).Must(x => x is null or "partName" or "-partName" or "partNumber" or "-partNumber" or "updatedAt" or "-updatedAt" or "createdAt" or "-createdAt");
        RuleFor(x => x.Status).Must(x => x is null || PartHandlerSupport.DirectoryStatuses.Contains(x)).WithMessage($"Status must be one of: {string.Join(", ", PartHandlerSupport.DirectoryStatuses)}.");
        RuleFor(x => x.PpapLevel).InclusiveBetween(1, 5);
        RuleFor(x => x.Classification).Must(x => x is null || PartHandlerSupport.TryClassification(x, out _)).WithMessage("Classification must be Standard, Critical, or Safety.");
    }
}

public sealed class CreatePartCommandValidator : AbstractValidator<CreatePartCommand>
{
    public CreatePartCommandValidator()
    {
        RuleFor(x => x.PartName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PartNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DrawingRevision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.PpapLevel).InclusiveBetween(1, 5);
        RuleFor(x => x.Classification).Must(x => x is null || PartHandlerSupport.TryClassification(x, out _)).WithMessage("Classification must be Standard, Critical, or Safety.");
    }
}

public sealed class GetPpapPartsSummaryQueryValidator : AbstractValidator<GetPpapPartsSummaryQuery>;

public sealed class UpdatePartCommandValidator : AbstractValidator<UpdatePartCommand>
{
    public UpdatePartCommandValidator()
    {
        RuleFor(x => x.PartId).NotEmpty();
        RuleFor(x => x.PartName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.PartNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DrawingRevision).NotEmpty().MaximumLength(50);
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.PpapLevel).InclusiveBetween(1, 5);
        RuleFor(x => x.Classification).Must(x => x is null || PartHandlerSupport.TryClassification(x, out _)).WithMessage("Classification must be Standard, Critical, or Safety.");
    }
}

public sealed class AssignPartTemplateCommandValidator : AbstractValidator<AssignPartTemplateCommand>
{
    public AssignPartTemplateCommandValidator()
    {
        RuleFor(x => x.PartId).NotEmpty();
        RuleFor(x => x.RuleTemplateId).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
    }
}

public sealed class CreateDraftFromPartCommandValidator : AbstractValidator<CreateDraftFromPartCommand>
{
    public CreateDraftFromPartCommandValidator()
    {
        RuleFor(x => x.PartId).NotEmpty();
        RuleFor(x => x.PpapLevel).InclusiveBetween(1, 5);
    }
}
