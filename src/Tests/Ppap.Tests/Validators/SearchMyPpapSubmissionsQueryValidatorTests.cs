using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Features.v1.Submissions.SearchMyPpapSubmissions;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class SearchMyPpapSubmissionsQueryValidatorTests
{
    private readonly SearchMyPpapSubmissionsQueryValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_QueryIsValid()
    {
        var query = new SearchMyPpapSubmissionsQuery
        {
            Search = "PN-100",
            Status = "Draft",
            PageNumber = 1,
            PageSize = 20,
            Sort = "-createdAt"
        };

        var result = _sut.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("Pending")]
    public void Validate_Should_Fail_When_StatusIsInvalid(string status)
    {
        var result = _sut.Validate(new SearchMyPpapSubmissionsQuery { Status = status });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "Status");
    }

    [Theory]
    [InlineData(0, 20, "PageNumber")]
    [InlineData(1, 0, "PageSize")]
    [InlineData(1, 101, "PageSize")]
    public void Validate_Should_Fail_When_PagingIsInvalid(int pageNumber, int pageSize, string propertyName)
    {
        var query = new SearchMyPpapSubmissionsQuery { PageNumber = pageNumber, PageSize = pageSize };

        var result = _sut.Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == propertyName);
    }

    [Fact]
    public void Validate_Should_Fail_When_SortIsInvalid()
    {
        var result = _sut.Validate(new SearchMyPpapSubmissionsQuery { Sort = "invalid" });

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "Sort");
    }
}
