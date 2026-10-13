using System.Text.Json;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Runtime;
using FluentValidation;

namespace DES.Modules.Rules.Features;

public sealed class SearchTemplatesQueryValidator : AbstractValidator<SearchTemplatesQuery>
{
    public SearchTemplatesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Sort).Must(x => x is null or "name" or "-name" or "created" or "-created");
    }
}

public sealed class CreateTemplateCommandValidator : AbstractValidator<CreateTemplateCommand>
{
    public CreateTemplateCommandValidator()
    {
        RuleFor(x => x.StableCode).NotEmpty().MaximumLength(64).Matches("^[A-Za-z0-9_.-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class ConfigureTemplateDraftCommandValidator : AbstractValidator<ConfigureTemplateDraftCommand>
{
    public ConfigureTemplateDraftCommandValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty();
        RuleFor(x => x.VersionId).NotEmpty();
        RuleFor(x => x.ExpectedAggregateVersion).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.DocumentRequirements).NotNull().Must(x => x is { Count: <= 50 }).WithMessage("No more than 50 document requirements are allowed.")
            .Must(NoDuplicateDocumentCodes).WithMessage("Document requirement codes must be unique.");
        RuleForEach(x => x.DocumentRequirements).ChildRules(item =>
        {
            item.RuleFor(x => x.DocumentCode).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9_.-]+$");
            item.RuleFor(x => x.Disposition).IsInEnum().When(x => x.Disposition.HasValue);
            item.RuleFor(x => x).Must(x => !x.Disposition.HasValue || x.IsRequired == (x.Disposition != TemplateDocumentDisposition.Retain))
                .WithMessage("IsRequired must match Disposition (Submit/Available=true, Retain=false).");
            item.RuleFor(x => x.SortOrder).InclusiveBetween(1, 1000);
        });
        RuleFor(x => x.RuleAssignments).NotNull().Must(x => x is { Count: <= 500 }).WithMessage("No more than 500 rule assignments are allowed.")
            .Must(NoDuplicateAssignmentKeys).WithMessage("Rule assignment keys must be unique.");
        RuleForEach(x => x.RuleAssignments).ChildRules(item =>
        {
            item.RuleFor(x => x.RuleVersionId).NotEmpty();
            item.RuleFor(x => x.DocumentCode).MaximumLength(32).Matches("^[A-Za-z0-9_.-]+$").When(x => !string.IsNullOrWhiteSpace(x.DocumentCode));
            item.RuleFor(x => x.ParametersJson).NotEmpty().Must(IsJsonObjectWithinLimit).WithMessage("ParametersJson must be a JSON object no larger than 16KB.");
        });
    }

    internal static bool IsJsonObjectWithinLimit(string? json)
    {
        try
        {
            RuleParameterContext.Parse(json);
            return json is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool NoDuplicateDocumentCodes(IReadOnlyList<TemplateDocumentRequirementInput>? items) => items is null || items.Select(x => x.DocumentCode?.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count() == items.Count;
    private static bool NoDuplicateAssignmentKeys(IReadOnlyList<TemplateRuleAssignmentInput>? items) => items is null || items.Select(x => (x.RuleVersionId, string.IsNullOrWhiteSpace(x.DocumentCode) ? null : x.DocumentCode.Trim().ToUpperInvariant())).Distinct().Count() == items.Count;
}

public sealed class SubmitTemplateForReviewCommandValidator : AbstractValidator<SubmitTemplateForReviewCommand>
{
    public SubmitTemplateForReviewCommandValidator() { RuleFor(x => x.TemplateId).NotEmpty(); RuleFor(x => x.VersionId).NotEmpty(); RuleFor(x => x.ExpectedAggregateVersion).GreaterThan(0); }
}

public sealed class PublishTemplateCommandValidator : AbstractValidator<PublishTemplateCommand>
{
    public PublishTemplateCommandValidator() { RuleFor(x => x.TemplateId).NotEmpty(); RuleFor(x => x.VersionId).NotEmpty(); RuleFor(x => x.ExpectedAggregateVersion).GreaterThan(0); }
}

public sealed class RetireTemplateCommandValidator : AbstractValidator<RetireTemplateCommand>
{
    public RetireTemplateCommandValidator() { RuleFor(x => x.TemplateId).NotEmpty(); RuleFor(x => x.VersionId).NotEmpty(); RuleFor(x => x.ExpectedAggregateVersion).GreaterThan(0); }
}

public sealed class ClonePublishedTemplateCommandValidator : AbstractValidator<ClonePublishedTemplateCommand>
{
    public ClonePublishedTemplateCommandValidator() { RuleFor(x => x.TemplateId).NotEmpty(); RuleFor(x => x.VersionId).NotEmpty(); RuleFor(x => x.ExpectedAggregateVersion).GreaterThan(0); }
}
