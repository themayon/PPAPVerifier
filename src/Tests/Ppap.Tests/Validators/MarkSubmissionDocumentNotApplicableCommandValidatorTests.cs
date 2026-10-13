using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Features.v1.Submissions.MarkSubmissionDocumentNotApplicable;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class MarkSubmissionDocumentNotApplicableCommandValidatorTests
{
    private readonly MarkSubmissionDocumentNotApplicableCommandValidator _sut = new();

    [Fact]
    public void Validate_Should_Pass_When_CommandIsValid()
    {
        var command = new MarkSubmissionDocumentNotApplicableCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Not required");

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Validate_Should_Fail_When_CommandIsInvalid()
    {
        var command = new MarkSubmissionDocumentNotApplicableCommand(
            Guid.Empty,
            Guid.Empty,
            new string('x', PpapSubmissionDocument.NotApplicableReasonMaxLength + 1));

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "SubmissionId");
        result.Errors.ShouldContain(x => x.PropertyName == "DocumentId");
        result.Errors.ShouldContain(x => x.PropertyName == "Reason");
    }

    [Fact]
    public void Validate_Should_Fail_When_ReasonIsEmpty()
    {
        var command = new MarkSubmissionDocumentNotApplicableCommand(
            Guid.NewGuid(), Guid.NewGuid(), "");

        var result = _sut.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "Reason");
    }
}
