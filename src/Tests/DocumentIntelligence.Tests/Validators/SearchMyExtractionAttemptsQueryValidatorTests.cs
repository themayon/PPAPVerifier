using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.SearchMyExtractionAttempts;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Validators;

public sealed class SearchMyExtractionAttemptsQueryValidatorTests
{
    private readonly SearchMyExtractionAttemptsQueryValidator _validator = new();

    [Fact]
    public void Validate_Should_Succeed_When_QueryIsValid()
    {
        SearchMyExtractionAttemptsQuery query = new()
        {
            Status = "Processing",
            PageNumber = 1,
            PageSize = 100,
            Sort = "-updatedAt"
        };

        _validator.Validate(query).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Validate_Should_Fail_When_PagingIsInvalid(int pageNumber, int pageSize)
    {
        SearchMyExtractionAttemptsQuery query = new() { PageNumber = pageNumber, PageSize = pageSize };

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Should_Fail_When_StatusIsUnknown()
    {
        SearchMyExtractionAttemptsQuery query = new() { Status = "Unknown" };

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Should_Fail_When_SortIsUnknown()
    {
        SearchMyExtractionAttemptsQuery query = new() { Sort = "provider" };

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }
}
