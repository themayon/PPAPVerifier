using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapSubmissionDocumentTests
{
    [Fact]
    public void PpapDocumentCodes_Should_ContainExactlySupportedPrototypeCodes()
    {
        string[] expected =
        [
            "DWG", "ECD", "CEA", "DFMEA", "PFD", "PFMEA", "CP", "MSA", "DIMR", "IMDS",
            "MPTR", "IPS", "QLD", "AAR", "SAMP", "MSAM", "CAID", "APDS", "CSR", "PSW"
        ];

        PpapDocumentCodes.All.Count.ShouldBe(20);
        PpapDocumentCodes.All.OrderBy(x => x).ShouldBe(expected.OrderBy(x => x));
    }

    [Fact]
    public void AddDocumentSlot_Should_CreatePendingSlotAndIncrementVersion_When_CodeIsSupported()
    {
        PpapSubmission submission = CreateDraft();

        PpapSubmissionDocument document = submission.AddDocumentSlot(" dwg ");

        document.Id.ShouldNotBe(Guid.Empty);
        document.SubmissionId.ShouldBe(submission.Id);
        document.TenantId.ShouldBe(submission.TenantId);
        document.DocumentCode.ShouldBe("DWG");
        document.IsRequired.ShouldBeTrue();
        document.Disposition.ShouldBe(SubmissionDocumentDisposition.Submit);
        document.Status.ShouldBe(SubmissionDocumentStatus.PendingUpload);
        document.FileAssetId.ShouldBeNull();
        submission.Documents.ShouldHaveSingleItem();
        submission.Version.ShouldBe(1);
    }

    [Fact]
    public void AddDocumentSlot_Should_ReturnExistingSlotWithoutIncrementingVersion_When_CodeAlreadyExists()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument original = submission.AddDocumentSlot("DFMEA", SubmissionDocumentDisposition.Submit);

        PpapSubmissionDocument repeated = submission.AddDocumentSlot("dfmea", SubmissionDocumentDisposition.Submit);

        repeated.ShouldBeSameAs(original);
        submission.Documents.Count.ShouldBe(1);
        submission.Version.ShouldBe(1);
    }

    [Fact]
    public void AddDocumentSlot_Should_ThrowConflict_When_DuplicateHasDifferentRequiredSetting()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument original = submission.AddDocumentSlot("DFMEA", SubmissionDocumentDisposition.Submit);

        CustomException exception = Should.Throw<CustomException>(() =>
            submission.AddDocumentSlot("dfmea", SubmissionDocumentDisposition.Retain));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        original.IsRequired.ShouldBeTrue();
        submission.Documents.Count.ShouldBe(1);
        submission.Version.ShouldBe(1);
    }

    [Fact]
    public void AddDocumentSlot_Should_Throw_When_CodeIsUnsupported()
    {
        PpapSubmission submission = CreateDraft();

        Should.Throw<ArgumentException>(() => submission.AddDocumentSlot("UNKNOWN"));

        submission.Documents.ShouldBeEmpty();
        submission.Version.ShouldBe(0);
    }

    [Fact]
    public void MarkDocumentNotApplicable_Should_SetReasonClearFileAndIncrementVersion()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("PSW");
        submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true);

        submission.MarkDocumentNotApplicable(document.Id, " Not supplied by customer ");

        document.Status.ShouldBe(SubmissionDocumentStatus.NotApplicable);
        document.NotApplicableReason.ShouldBe("Not supplied by customer");
        document.FileAssetId.ShouldBeNull();
        document.UpdatedAtUtc.ShouldNotBeNull();
        submission.Version.ShouldBe(3);
    }

    [Fact]
    public void MarkDocumentNotApplicable_Should_BeIdempotent_When_ExactRequestIsRepeated()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("PSW");
        submission.MarkDocumentNotApplicable(document.Id, "Not supplied");

        submission.MarkDocumentNotApplicable(document.Id, " Not supplied ");

        submission.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void MarkDocumentNotApplicable_Should_Throw_When_ReasonIsMissing(string reason)
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("CSR");

        Should.Throw<ArgumentException>(() => submission.MarkDocumentNotApplicable(document.Id, reason));

        document.Status.ShouldBe(SubmissionDocumentStatus.PendingUpload);
        submission.Version.ShouldBe(1);
    }

    [Fact]
    public void MarkDocumentNotApplicable_Should_Throw_When_ReasonExceedsMaximumLength()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("CSR");

        Should.Throw<ArgumentOutOfRangeException>(() => submission.MarkDocumentNotApplicable(
            document.Id,
            new string('x', PpapSubmissionDocument.NotApplicableReasonMaxLength + 1)));

        document.Status.ShouldBe(SubmissionDocumentStatus.PendingUpload);
        submission.Version.ShouldBe(1);
    }

    [Fact]
    public void ApplyFinalizedFile_Should_MarkAvailableAndIncrementVersion_When_FileIsAvailable()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("DIMR");
        Guid fileAssetId = Guid.NewGuid();

        submission.ApplyFinalizedFile(document.Id, fileAssetId, true);

        document.Status.ShouldBe(SubmissionDocumentStatus.Available);
        document.FileAssetId.ShouldBe(fileAssetId);
        submission.Version.ShouldBe(2);
    }

    [Fact]
    public void ApplyFinalizedFile_Should_MarkQuarantined_When_FileIsQuarantined()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("IMDS");
        Guid fileAssetId = Guid.NewGuid();

        submission.ApplyFinalizedFile(document.Id, fileAssetId, false);

        document.Status.ShouldBe(SubmissionDocumentStatus.Quarantined);
        document.FileAssetId.ShouldBe(fileAssetId);
        submission.Version.ShouldBe(2);
    }

    [Fact]
    public void ApplyFinalizedFile_Should_BeIdempotent_When_ExactRequestIsRepeated()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("MSA");
        Guid fileAssetId = Guid.NewGuid();
        submission.ApplyFinalizedFile(document.Id, fileAssetId, true);

        submission.ApplyFinalizedFile(document.Id, fileAssetId, true);

        document.Status.ShouldBe(SubmissionDocumentStatus.Available);
        submission.Version.ShouldBe(2);
    }

    [Fact]
    public void ApplyFinalizedFile_Should_ThrowConflict_When_SameAssetHasDifferentFinalStatus()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("AAR");
        Guid fileAssetId = Guid.NewGuid();
        submission.ApplyFinalizedFile(document.Id, fileAssetId, true);

        CustomException exception = Should.Throw<CustomException>(() =>
            submission.ApplyFinalizedFile(document.Id, fileAssetId, false));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        submission.Version.ShouldBe(2);
    }

    [Fact]
    public void ApplyFinalizedFile_Should_ThrowConflict_When_DifferentAssetIsAlreadyAttached()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("AAR");
        submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true);

        CustomException exception = Should.Throw<CustomException>(() =>
            submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        submission.Version.ShouldBe(2);
    }

    [Fact]
    public void ApplyFinalizedFile_Should_ThrowConflict_When_DocumentIsNotApplicable()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("AAR");
        submission.MarkDocumentNotApplicable(document.Id, "Not required");

        CustomException exception = Should.Throw<CustomException>(() =>
            submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        submission.Version.ShouldBe(2);
    }

    [Fact]
    public void MarkExtractionRequestPublished_Should_RecordStableStateAndBeIdempotent_When_DocumentIsAvailable()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("DIMR");
        submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true);
        Guid eventId = Guid.CreateVersion7();
        DateTime publishedAtUtc = DateTime.UtcNow;

        document.MarkExtractionRequestPublished(eventId, publishedAtUtc).ShouldBeTrue();
        document.MarkExtractionRequestPublished(Guid.CreateVersion7(), publishedAtUtc.AddMinutes(1)).ShouldBeFalse();

        document.ExtractionRequestEventId.ShouldBe(eventId);
        document.ExtractionRequestPublishedAtUtc.ShouldBe(publishedAtUtc);
    }

    [Fact]
    public void MarkExtractionRequestPublished_Should_ThrowConflict_When_DocumentIsNotAvailable()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("DIMR");

        CustomException exception = Should.Throw<CustomException>(() =>
            document.MarkExtractionRequestPublished(Guid.CreateVersion7(), DateTime.UtcNow));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        document.ExtractionRequestEventId.ShouldBeNull();
        document.ExtractionRequestPublishedAtUtc.ShouldBeNull();
    }

    [Fact]
    public void DocumentMutations_Should_ThrowConflict_When_SubmissionIsSubmitted()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot("PFD");
        submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true);
        submission.Submit();

        Should.Throw<CustomException>(() => submission.AddDocumentSlot("PFMEA"));
        Should.Throw<CustomException>(() =>
            submission.MarkDocumentNotApplicable(document.Id, "Not applicable"));
        Should.Throw<CustomException>(() =>
            submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true));

        submission.Version.ShouldBe(3);
        document.Status.ShouldBe(SubmissionDocumentStatus.Available);
    }

    private static PpapSubmission CreateDraft() =>
        PpapSubmission.CreateDraft("tenant-1", Guid.NewGuid(), "Part", "PN-100", "A", 3);
}
