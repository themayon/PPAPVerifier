using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapSubmissionTests
{
    [Fact]
    public void CreateDraft_Should_TrimValuesAndInitializeDraftState_When_InputIsValid()
    {
        Guid ownerUserId = Guid.NewGuid();

        PpapSubmission submission = PpapSubmission.CreateDraft(
            " tenant-1 ", ownerUserId, " Part Name ", " PN-100 ", " A ", 3);

        submission.Id.ShouldNotBe(Guid.Empty);
        submission.TenantId.ShouldBe("tenant-1");
        submission.OwnerUserId.ShouldBe(ownerUserId);
        submission.PartName.ShouldBe("Part Name");
        submission.PartNumber.ShouldBe("PN-100");
        submission.DrawingRevision.ShouldBe("A");
        submission.PpapLevel.ShouldBe(3);
        submission.Status.ShouldBe(SubmissionStatus.Draft);
        submission.Version.ShouldBe(0);
        submission.CreatedAtUtc.ShouldNotBe(default);
        submission.UpdatedAtUtc.ShouldBeNull();
        submission.SubmittedAtUtc.ShouldBeNull();
        submission.CancelledAtUtc.ShouldBeNull();
    }

    [Fact]
    public void RemoveDocumentRevision_Should_ResetSlotToPendingUpload_When_Draft()
    {
        PpapSubmission submission = PpapSubmission.CreateDraft("tenant-1", Guid.NewGuid(), "Part", "PN-100", "A", 3);
        PpapSubmissionDocument document = submission.AddDocumentSlot("PFMEA");
        submission.ApplyFinalizedFile(document.Id, Guid.CreateVersion7(), true);
        document.MarkExtractionRequestPublished(Guid.CreateVersion7(), DateTime.UtcNow);
        int versionBefore = submission.Version;

        submission.RemoveDocumentRevision(document.Id);

        document.Status.ShouldBe(SubmissionDocumentStatus.PendingUpload);
        document.FileAssetId.ShouldBeNull();
        document.ExtractionRequestPublishedAtUtc.ShouldBeNull();
        document.ExtractionTerminalStatus.ShouldBeNull();
        submission.Version.ShouldBeGreaterThan(versionBefore);
    }

    [Fact]
    public void RemoveDocumentRevision_Should_Throw_When_SubmissionIsNotDraft()
    {
        PpapSubmission submission = PpapSubmission.CreateDraft("tenant-1", Guid.NewGuid(), "Part", "PN-100", "A", 3);
        PpapSubmissionDocument document = submission.AddDocumentSlot("PFMEA");
        submission.ApplyFinalizedFile(document.Id, Guid.CreateVersion7(), true);
        submission.Submit();

        Should.Throw<CustomException>(() => submission.RemoveDocumentRevision(document.Id))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void CreateDraft_Should_PersistTemplateSnapshot_When_BothIdsAreSupplied()
    {
        Guid templateId = Guid.NewGuid();
        Guid templateVersionId = Guid.NewGuid();

        PpapSubmission submission = PpapSubmission.CreateDraft(
            "tenant-1", Guid.NewGuid(), "Part", "PN-100", "A", 3, templateId, templateVersionId);

        submission.RuleTemplateId.ShouldBe(templateId);
        submission.RuleTemplateVersionId.ShouldBe(templateVersionId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateDraft_Should_Throw_When_OnlyOneTemplateSnapshotIdIsSupplied(bool includeTemplateId)
    {
        Guid? templateId = includeTemplateId ? Guid.NewGuid() : null;
        Guid? templateVersionId = includeTemplateId ? null : Guid.NewGuid();

        Should.Throw<ArgumentException>(() => PpapSubmission.CreateDraft(
            "tenant-1", Guid.NewGuid(), "Part", "PN-100", "A", 3, templateId, templateVersionId));
    }

    [Fact]
    public void UpdateDraft_Should_IncrementVersionAndPreserveTemplateSnapshot_When_SubmissionIsDraft()
    {
        Guid templateId = Guid.NewGuid();
        Guid templateVersionId = Guid.NewGuid();
        PpapSubmission submission = PpapSubmission.CreateDraft(
            "tenant-1", Guid.NewGuid(), "Part", "PN-100", "A", 3, templateId, templateVersionId);

        submission.UpdateDraft("Updated", "PN-200", "B", 4);

        submission.Version.ShouldBe(1);
        submission.RuleTemplateId.ShouldBe(templateId);
        submission.RuleTemplateVersionId.ShouldBe(templateVersionId);
    }

    [Fact]
    public void Submit_Should_IncrementVersion_When_SubmissionHasAvailableDocument()
    {
        PpapSubmission submission = CreateDraftWithAvailableDocument();
        int version = submission.Version;

        submission.Submit();

        submission.Version.ShouldBe(version + 1);
        submission.Status.ShouldBe(SubmissionStatus.Submitted);
    }

    [Fact]
    public void Submit_Should_ThrowConflict_When_SubmissionHasNoDocuments()
    {
        PpapSubmission submission = CreateDraft();

        CustomException exception = Should.Throw<CustomException>(submission.Submit);

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Submit_Should_ThrowConflict_When_SubmissionHasPendingDocument()
    {
        PpapSubmission submission = CreateDraftWithAvailableDocument();
        submission.AddDocumentSlot(DES.Modules.Ppap.Contracts.PpapDocumentCodes.Psw);

        CustomException exception = Should.Throw<CustomException>(submission.Submit);

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Submit_Should_AcceptPendingRetainDocument_When_AnotherDocumentIsAvailable()
    {
        PpapSubmission submission = CreateDraftWithAvailableDocument();
        submission.AddDocumentSlot(PpapDocumentCodes.Psw, SubmissionDocumentDisposition.Retain);

        submission.Submit();

        submission.Status.ShouldBe(SubmissionStatus.Submitted);
    }

    [Theory]
    [InlineData(SubmissionDocumentDisposition.Submit)]
    [InlineData(SubmissionDocumentDisposition.Available)]
    public void Submit_Should_ThrowConflict_When_UploadRequiredDocumentIsPending(SubmissionDocumentDisposition disposition)
    {
        PpapSubmission submission = CreateDraftWithAvailableDocument();
        submission.AddDocumentSlot(PpapDocumentCodes.Psw, disposition);

        CustomException exception = Should.Throw<CustomException>(submission.Submit);

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Submit_Should_ThrowConflict_When_SubmissionHasQuarantinedDocument()
    {
        PpapSubmission submission = CreateDraftWithAvailableDocument();
        PpapSubmissionDocument document = submission.AddDocumentSlot(DES.Modules.Ppap.Contracts.PpapDocumentCodes.Psw);
        submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), false);

        CustomException exception = Should.Throw<CustomException>(submission.Submit);

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Submit_Should_AcceptNotApplicableDocument_When_AnotherDocumentIsAvailable()
    {
        PpapSubmission submission = CreateDraftWithAvailableDocument();
        PpapSubmissionDocument document = submission.AddDocumentSlot(DES.Modules.Ppap.Contracts.PpapDocumentCodes.Psw);
        submission.MarkDocumentNotApplicable(document.Id, "Not required");

        submission.Submit();

        submission.Status.ShouldBe(SubmissionStatus.Submitted);
    }

    [Fact]
    public void Cancel_Should_IncrementVersion_When_SubmissionIsDraft()
    {
        PpapSubmission submission = CreateDraft();

        submission.Cancel();

        submission.Version.ShouldBe(1);
    }

    [Fact]
    public void UpdateDraft_Should_ThrowConflict_When_SubmissionIsSubmitted()
    {
        PpapSubmission submission = CreateDraftWithAvailableDocument();
        submission.Submit();

        CustomException exception = Should.Throw<CustomException>(() =>
            submission.UpdateDraft("Updated", "PN-200", "B", 4));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Submit_Should_ThrowConflict_When_SubmissionIsCancelled()
    {
        PpapSubmission submission = CreateDraft();
        submission.Cancel();

        CustomException exception = Should.Throw<CustomException>(submission.Submit);

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Cancel_Should_ThrowConflict_When_SubmissionIsSubmitted()
    {
        PpapSubmission submission = CreateDraftWithAvailableDocument();
        submission.Submit();

        CustomException exception = Should.Throw<CustomException>(submission.Cancel);

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static PpapSubmission CreateDraft() =>
        PpapSubmission.CreateDraft("tenant-1", Guid.NewGuid(), "Part", "PN-100", "A", 3);

    private static PpapSubmission CreateDraftWithAvailableDocument()
    {
        PpapSubmission submission = CreateDraft();
        PpapSubmissionDocument document = submission.AddDocumentSlot(DES.Modules.Ppap.Contracts.PpapDocumentCodes.Dwg);
        submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true);
        return submission;
    }
}
