using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Features.v1.Credits.SearchMyCreditTopupRequests;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class SearchMyCreditTopupRequestsQueryValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("Pending")]
    [InlineData("approved")]
    [InlineData("Rejected")]
    public void Validate_Should_Pass_When_QueryIsValid(string? status)
    {
        SearchMyCreditTopupRequestsQueryValidator validator = new();
        SearchMyCreditTopupRequestsQuery query = new()
        {
            Status = status,
            PageNumber = 1,
            PageSize = 100
        };

        FluentValidation.Results.ValidationResult result = validator.Validate(query);

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Validate_Should_Fail_When_PaginationIsOutsideLimits(int pageNumber, int pageSize)
    {
        SearchMyCreditTopupRequestsQueryValidator validator = new();
        SearchMyCreditTopupRequestsQuery query = new()
        {
            PageNumber = pageNumber,
            PageSize = pageSize
        };

        FluentValidation.Results.ValidationResult result = validator.Validate(query);

        result.IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Should_Fail_When_StatusIsUnsupported()
    {
        SearchMyCreditTopupRequestsQueryValidator validator = new();
        SearchMyCreditTopupRequestsQuery query = new()
        {
            Status = "Expired",
            PageNumber = 1,
            PageSize = 20
        };

        FluentValidation.Results.ValidationResult result = validator.Validate(query);

        result.Errors.ShouldContain(x => x.PropertyName == "Status");
    }
}
