using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Features.v1.Submissions.CreateDraftSubmission;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class CreateDraftSubmissionCommandValidatorTests
{
    private readonly CreateDraftSubmissionCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_CommandIsValid()
    {
        var command = new CreateDraftSubmissionCommand("Part", "PN-100", "A", 3);

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_Pass_When_OptionalTemplateIdIsValid()
    {
        CreateDraftSubmissionCommand command = new("Part", "PN-100", "A", 3, Guid.NewGuid());

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_Fail_When_OptionalTemplateIdIsEmpty()
    {
        CreateDraftSubmissionCommand command = new("Part", "PN-100", "A", 3, Guid.Empty);

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "RuleTemplateId");
    }

    [Theory]
    [InlineData("", "PN-100", "A", 3, "PartName")]
    [InlineData("Part", "", "A", 3, "PartNumber")]
    [InlineData("Part", "PN-100", "", 3, "DrawingRevision")]
    [InlineData("Part", "PN-100", "A", 0, "PpapLevel")]
    [InlineData("Part", "PN-100", "A", 6, "PpapLevel")]
    public void Validate_Should_Fail_When_CommandIsInvalid(
        string partName,
        string partNumber,
        string drawingRevision,
        int ppapLevel,
        string propertyName)
    {
        var command = new CreateDraftSubmissionCommand(partName, partNumber, drawingRevision, ppapLevel);

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == propertyName);
    }

    [Fact]
    public void Validate_Should_Fail_When_StringsExceedMaximumLengths()
    {
        var command = new CreateDraftSubmissionCommand(
            new string('a', 201),
            new string('b', 101),
            new string('c', 51),
            3);

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "PartName");
        result.Errors.ShouldContain(x => x.PropertyName == "PartNumber");
        result.Errors.ShouldContain(x => x.PropertyName == "DrawingRevision");
    }
}
