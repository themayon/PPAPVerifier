using DES.Modules.Ppap.Contracts.v1.Parts;
using DES.Modules.Ppap.Features.v1.Parts;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class PpapPartValidatorTests
{
    [Fact]
    public void CreatePartValidator_Should_Fail_When_FieldsExceedLimits()
    {
        CreatePartCommandValidator validator = new();
        CreatePartCommand command = new(new string('a', 201), new string('b', 101), new string('c', 51));

        FluentValidation.Results.ValidationResult result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.Select(x => x.PropertyName).ShouldContain("PartName");
        result.Errors.Select(x => x.PropertyName).ShouldContain("PartNumber");
        result.Errors.Select(x => x.PropertyName).ShouldContain("DrawingRevision");
    }

    [Fact]
    public void SearchPartsValidator_Should_Fail_When_PagingOrSortIsInvalid()
    {
        SearchMyPpapPartsQueryValidator validator = new();
        SearchMyPpapPartsQuery query = new() { PageNumber = 0, PageSize = 101, Sort = "unknown" };

        FluentValidation.Results.ValidationResult result = validator.Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBe(3);
    }

    [Fact]
    public void AssignTemplateValidator_Should_Fail_When_IdsAndVersionAreEmpty()
    {
        AssignPartTemplateCommandValidator validator = new();

        FluentValidation.Results.ValidationResult result = validator.Validate(new AssignPartTemplateCommand(Guid.Empty, Guid.Empty, 0));

        result.IsValid.ShouldBeFalse();
        result.Errors.Count.ShouldBe(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void CreateDraftFromPartValidator_Should_Fail_When_LevelIsOutsideRange(int level)
    {
        CreateDraftFromPartCommandValidator validator = new();

        validator.Validate(new CreateDraftFromPartCommand(Guid.NewGuid(), level)).IsValid.ShouldBeFalse();
    }
}
