using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;
using FluentValidation.TestHelper;
using Xunit;

namespace DocumentIntelligence.Tests.Validators;

public sealed class RequestDocumentExtractionCommandValidatorTests
{
    private readonly RequestDocumentExtractionCommandValidator _validator = new();

    [Fact]
    public void Validate_Should_Succeed_When_CommandIsValid()
    {
        RequestDocumentExtractionCommand command = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "PSW");

        _validator.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_Should_Fail_When_IdentifiersAndCodeAreInvalid()
    {
        RequestDocumentExtractionCommand command = new(Guid.Empty, Guid.Empty, Guid.Empty, string.Empty);

        TestValidationResult<RequestDocumentExtractionCommand> result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(x => x.OwnerUserId);
        result.ShouldHaveValidationErrorFor(x => x.SubmissionDocumentId);
        result.ShouldHaveValidationErrorFor(x => x.SourceFileAssetId);
        result.ShouldHaveValidationErrorFor(x => x.DocumentCode);
    }
}
