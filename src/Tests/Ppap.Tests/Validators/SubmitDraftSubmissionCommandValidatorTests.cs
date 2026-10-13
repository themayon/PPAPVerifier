using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Features.v1.Submissions.SubmitDraftSubmission;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class SubmitDraftSubmissionCommandValidatorTests
{
    private readonly SubmitDraftSubmissionCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_CommandIsValid()
    {
        var result = _sut.Validate(new SubmitDraftSubmissionCommand(Guid.NewGuid(), 0));

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_Fail_When_IdAndVersionAreInvalid()
    {
        var result = _sut.Validate(new SubmitDraftSubmissionCommand(Guid.Empty, -1));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "Id");
        result.Errors.ShouldContain(x => x.PropertyName == "ExpectedVersion");
    }
}
