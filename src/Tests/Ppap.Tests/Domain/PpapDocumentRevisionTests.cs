using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapDocumentRevisionTests
{
    [Fact]
    public void Create_Should_InitializeUploadingCurrentRevision()
    {
        Guid documentId = Guid.CreateVersion7();
        Guid ownerId = Guid.CreateVersion7();

        PpapDocumentRevision revision = PpapDocumentRevision.Create(
            "tenant-1",
            documentId,
            1,
            ownerId,
            "dimensional-results.pdf",
            "application/pdf",
            1024);

        revision.Id.ShouldNotBe(Guid.Empty);
        revision.SubmissionDocumentId.ShouldBe(documentId);
        revision.OwnerUserId.ShouldBe(ownerId);
        revision.UploadStatus.ShouldBe(DocumentUploadStatus.Uploading);
        revision.ProcessingStatus.ShouldBe(DocumentProcessingStatus.NotQueued);
        revision.IsCurrent.ShouldBeTrue();
    }

    [Fact]
    public void QueueExtraction_Should_BeIdempotent_ForSameEvent()
    {
        PpapDocumentRevision revision = CreateAvailableRevision();
        Guid eventId = Guid.CreateVersion7();
        DateTime utcNow = DateTime.UtcNow;

        revision.QueueExtraction(eventId, utcNow).ShouldBeTrue();
        revision.QueueExtraction(eventId, utcNow).ShouldBeTrue();

        revision.ExtractionRequestEventId.ShouldBe(eventId);
        revision.ProcessingStatus.ShouldBe(DocumentProcessingStatus.Queued);
    }

    [Fact]
    public void RecordExtractionCompleted_Should_RejectConflictingTerminalResult()
    {
        PpapDocumentRevision revision = CreateAvailableRevision();
        revision.QueueExtraction(Guid.CreateVersion7(), DateTime.UtcNow);
        revision.RecordExtractionCompleted(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            Guid.CreateVersion7(),
            "aspect-pdf",
            "aspect-v1",
            DateTime.UtcNow);

        Should.Throw<CustomException>(() => revision.RecordExtractionFailed(
            Guid.CreateVersion7(),
            "PROVIDER_FAILED",
            DateTime.UtcNow));
    }

    [Fact]
    public void Supersede_Should_RejectNonterminalProcessingRevision()
    {
        PpapDocumentRevision revision = CreateAvailableRevision();
        revision.QueueExtraction(Guid.CreateVersion7(), DateTime.UtcNow);

        Should.Throw<CustomException>(() => revision.Supersede(DateTime.UtcNow));
    }

    private static PpapDocumentRevision CreateAvailableRevision()
    {
        PpapDocumentRevision revision = PpapDocumentRevision.Create(
            "tenant-1",
            Guid.CreateVersion7(),
            1,
            Guid.CreateVersion7(),
            "document.pdf",
            "application/pdf",
            1024);
        revision.ApplyFinalizedFile(Guid.CreateVersion7(), "application/pdf", 1024, true, DateTime.UtcNow);
        return revision;
    }
}
