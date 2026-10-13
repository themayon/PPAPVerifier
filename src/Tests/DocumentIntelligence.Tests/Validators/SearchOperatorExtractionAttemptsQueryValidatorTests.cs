using DES.Modules.DocumentIntelligence.Contracts.v1.Operations;
using DES.Modules.DocumentIntelligence.Features.v1.Operations.SearchOperatorExtractionAttempts;
using Shouldly;
using Xunit;

namespace DocumentIntelligence.Tests.Validators;

public sealed class SearchOperatorExtractionAttemptsQueryValidatorTests
{
    private readonly SearchOperatorExtractionAttemptsQueryValidator _validator = new();

    [Fact]
    public void Validate_Should_Succeed_When_QueryAndFiltersAreValid()
    {
        SearchOperatorExtractionAttemptsQuery query = new()
        {
            Status = "DeadLettered",
            DocumentCode = "PSW",
            OwnerUserId = Guid.CreateVersion7(),
            PageNumber = 1,
            PageSize = 100,
            Sort = "-ownerUserId"
        };

        _validator.Validate(query).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public void Validate_Should_Fail_When_PagingIsInvalid(int pageNumber, int pageSize)
    {
        SearchOperatorExtractionAttemptsQuery query = new() { PageNumber = pageNumber, PageSize = pageSize };

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Should_Fail_When_StatusIsUnknown()
    {
        SearchOperatorExtractionAttemptsQuery query = new() { Status = "Unknown" };

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Should_Fail_When_DocumentCodeIsTooLong()
    {
        SearchOperatorExtractionAttemptsQuery query = new() { DocumentCode = new string('P', 33) };

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Should_Fail_When_OwnerUserIdIsEmpty()
    {
        SearchOperatorExtractionAttemptsQuery query = new() { OwnerUserId = Guid.Empty };

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Validate_Should_Fail_When_SortIsUnknown()
    {
        SearchOperatorExtractionAttemptsQuery query = new() { Sort = "provider" };

        _validator.Validate(query).IsValid.ShouldBeFalse();
    }
}
