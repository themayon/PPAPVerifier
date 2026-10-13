using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Submissions.SearchMyPpapSubmissions;

public sealed class SearchMyPpapSubmissionsQueryValidator : AbstractValidator<SearchMyPpapSubmissionsQuery>
{
    private static readonly string[] Sorts =
    [
        "createdAt",
        "-createdAt",
        "updatedAt",
        "-updatedAt",
        "partName",
        "-partName",
        "partNumber",
        "-partNumber",
        "drawingRevision",
        "-drawingRevision",
        "ppapLevel",
        "-ppapLevel",
        "status",
        "-status"
    ];

    public SearchMyPpapSubmissionsQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.PageNumber).NotNull().InclusiveBetween(1, int.MaxValue);
        RuleFor(x => x.PageSize).NotNull().InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(value => Enum.TryParse<SubmissionStatus>(value, true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status));
        RuleFor(x => x.Sort)
            .Must(value => Sorts.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Sort));
    }
}
