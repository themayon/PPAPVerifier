using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Features.v1.Submissions.UpdateDraftSubmission;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class UpdateDraftSubmissionCommandValidatorTests
{
    private readonly UpdateDraftSubmissionCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_CommandIsValid()
    {
        var command = new UpdateDraftSubmissionCommand(Guid.NewGuid(), 0, "Part", "PN-100", "A", 3);

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_Fail_When_IdAndVersionAreInvalid()
    {
        var command = new UpdateDraftSubmissionCommand(Guid.Empty, -1, "Part", "PN-100", "A", 3);

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "Id");
        result.Errors.ShouldContain(x => x.PropertyName == "ExpectedVersion");
    }

    [Theory]
    [InlineData("", "PN-100", "A", 3, "PartName")]
    [InlineData("Part", "", "A", 3, "PartNumber")]
    [InlineData("Part", "PN-100", "", 3, "DrawingRevision")]
    [InlineData("Part", "PN-100", "A", 0, "PpapLevel")]
    [InlineData("Part", "PN-100", "A", 6, "PpapLevel")]
    public void Validate_Should_Fail_When_FieldsAreInvalid(
        string partName,
        string partNumber,
        string drawingRevision,
        int ppapLevel,
        string propertyName)
    {
        var command = new UpdateDraftSubmissionCommand(
            Guid.NewGuid(), 0, partName, partNumber, drawingRevision, ppapLevel);

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == propertyName);
    }
}
