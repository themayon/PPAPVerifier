using DES.Modules.Ppap.Contracts.v1.Jobs;
using DES.Modules.Ppap.Domain;
using FluentValidation;

namespace DES.Modules.Ppap.Features.v1.Jobs.SearchPpapJobs;

public sealed class SearchPpapJobsQueryValidator : AbstractValidator<SearchPpapJobsQuery>
{
    private static readonly string[] Sorts =
    [
        "createdAtUtc",
        "-createdAtUtc",
        "status",
        "-status",
        "progressPercent",
        "-progressPercent",
        "partName",
        "-partName",
        "partNumber",
        "-partNumber",
        "ownerUserId",
        "-ownerUserId"
    ];

    public SearchPpapJobsQueryValidator()
    {
        RuleFor(x => x.PageNumber).NotNull().InclusiveBetween(1, int.MaxValue);
        RuleFor(x => x.PageSize).NotNull().InclusiveBetween(1, 100);
        RuleFor(x => x.Status)
            .Must(value => Enum.TryParse<PpapJobStatus>(value, true, out _))
            .When(x => !string.IsNullOrWhiteSpace(x.Status));
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Sort)
            .Must(value => Sorts.Contains(value, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Sort));
    }
}
