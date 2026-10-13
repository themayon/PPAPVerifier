using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Features.v1.Credits.ApproveCreditTopupRequest;
using DES.Modules.Ppap.Features.v1.Credits.CreateCreditTopupRequest;
using DES.Modules.Ppap.Features.v1.Credits.RejectCreditTopupRequest;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Validators;

public sealed class CreditTopupCommandValidatorTests
{
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void CreateValidate_Should_Pass_When_CommandIsValid(int credits)
    {
        CreateCreditTopupRequestCommandValidator validator = new();
        CreateCreditTopupRequestCommand command = new(credits, "payment", "note");

        var result = validator.Validate(command);

        result.IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(101)]
    public void CreateValidate_Should_Fail_When_CreditPackageIsInvalid(int credits)
    {
        CreateCreditTopupRequestCommandValidator validator = new();
        CreateCreditTopupRequestCommand command = new(credits, null, null);

        var result = validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(x => x.PropertyName == "CreditsRequested");
    }

    [Fact]
    public void CreateValidate_Should_Fail_When_TextExceedsMaximumLengths()
    {
        CreateCreditTopupRequestCommandValidator validator = new();
        CreateCreditTopupRequestCommand command = new(
            10,
            new string('p', CreditTopupRequest.MaxPaymentReferenceLength + 1),
            new string('n', CreditTopupRequest.MaxNoteLength + 1));

        var result = validator.Validate(command);

        result.Errors.ShouldContain(x => x.PropertyName == "PaymentReference");
        result.Errors.ShouldContain(x => x.PropertyName == "Note");
    }

    [Fact]
    public void ApproveValidate_Should_Fail_When_CommandIsInvalid()
    {
        ApproveCreditTopupRequestCommandValidator validator = new();
        ApproveCreditTopupRequestCommand command = new(
            Guid.Empty,
            new string('d', CreditTopupRequest.MaxDecisionNoteLength + 1));

        var result = validator.Validate(command);

        result.Errors.ShouldContain(x => x.PropertyName == "Id");
        result.Errors.ShouldContain(x => x.PropertyName == "DecisionNote");
    }

    [Fact]
    public void RejectValidate_Should_Fail_When_CommandIsInvalid()
    {
        RejectCreditTopupRequestCommandValidator validator = new();
        RejectCreditTopupRequestCommand command = new(
            Guid.Empty,
            new string('d', CreditTopupRequest.MaxDecisionNoteLength + 1));

        var result = validator.Validate(command);

        result.Errors.ShouldContain(x => x.PropertyName == "Id");
        result.Errors.ShouldContain(x => x.PropertyName == "DecisionNote");
    }
}
