using DES.Modules.Ppap.Contracts.v1.Jobs;
using DES.Modules.Ppap.Features.v1.Jobs.SearchPpapJobs;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class SearchPpapJobsQueryValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Queued")]
    [InlineData("processing")]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public void Validate_Should_Pass_When_QueryIsValid(string? status)
    {
        SearchPpapJobsQueryValidator validator = new();
        SearchPpapJobsQuery query = new()
        {
            Status = status,
            Search = "bracket",
            PageNumber = 1,
            PageSize = 100,
            Sort = "-createdAtUtc"
        };

        FluentValidation.Results.ValidationResult result = validator.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_Fail_When_ValuesAreUnsupported()
    {
        SearchPpapJobsQueryValidator validator = new();
        SearchPpapJobsQuery query = new()
        {
            Status = "Waiting",
            Search = new string('x', 201),
            PageNumber = 0,
            PageSize = 101,
            Sort = "errorCode"
        };

        FluentValidation.Results.ValidationResult result = validator.Validate(query);

        result.Errors.ShouldContain(x => x.PropertyName == "Status");
        result.Errors.ShouldContain(x => x.PropertyName == "Search");
        result.Errors.ShouldContain(x => x.PropertyName == "PageNumber");
        result.Errors.ShouldContain(x => x.PropertyName == "PageSize");
        result.Errors.ShouldContain(x => x.PropertyName == "Sort");
    }
}
