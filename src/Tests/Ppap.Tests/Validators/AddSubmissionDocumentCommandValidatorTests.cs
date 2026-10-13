using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Features.v1.Submissions.AddSubmissionDocument;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class AddSubmissionDocumentCommandValidatorTests
{
    private readonly AddSubmissionDocumentCommandValidator _sut = new();

    [Theory]
    [InlineData("DWG")]
    [InlineData(" psw ")]
    [InlineData("DFMEA")]
    public void Validate_Should_Pass_When_CommandUsesSupportedCode(string code)
    {
        var result = _sut.Validate(new AddSubmissionDocumentCommand(Guid.NewGuid(), code));

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("UNKNOWN")]
    public void Validate_Should_Fail_When_DocumentCodeIsInvalid(string code)
    {
        var result = _sut.Validate(new AddSubmissionDocumentCommand(Guid.NewGuid(), code));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "DocumentCode");
    }

    [Fact]
    public void Validate_Should_Fail_When_SubmissionIdIsEmpty()
    {
        var result = _sut.Validate(new AddSubmissionDocumentCommand(Guid.Empty, "DWG"));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "SubmissionId");
    }
}
