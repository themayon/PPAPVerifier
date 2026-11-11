using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Features.v1.Submissions.DeleteDocumentRevision;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Features;

public sealed class DeleteDocumentRevisionFileCleanupTests
{
    private const string TenantId = "tenant-1";
    private const string ContentType = "application/pdf";
    private const long SizeBytes = 1024;

    [Fact]
    public void CollectFileAssetIds_Should_IncludeSourceAndAllExtractionArtifacts_When_RevisionCompleted()
    {
        (PpapSubmission submission, PpapSubmissionDocument document) = CreateDocument();
        Guid sourceId = Guid.CreateVersion7();
        Guid attemptId = Guid.CreateVersion7();
        Guid rawId = Guid.CreateVersion7();
        Guid workbookId = Guid.CreateVersion7();
        Guid previewId = Guid.CreateVersion7();
        Guid canonicalId = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;

        submission.ApplyFinalizedFile(document.Id, sourceId, true);
        document.MarkExtractionRequestPublished(Guid.CreateVersion7(), now);
        document.RecordExtractionCompleted(TenantId, document.DocumentCode, attemptId, canonicalId, rawId, workbookId, now);
        PpapDocumentRevision revision = CreateRevision(submission, document, sourceId, now);
        revision.QueueExtraction(Guid.CreateVersion7(), now);
        revision.SetPreviewPdf(previewId, now);
        revision.RecordExtractionCompleted(attemptId, canonicalId, rawId, workbookId, "gemini", "model", now);

        Guid[] ids = DeleteDocumentRevisionCommandHandler.CollectFileAssetIds(revision, document);

        ids.ShouldBe([sourceId, rawId, workbookId, previewId], ignoreOrder: true);
    }

    [Fact]
    public void CollectFileAssetIds_Should_ExcludeDocumentArtifacts_When_TheyBelongToAnEarlierFile()
    {
        (PpapSubmission submission, PpapSubmissionDocument document) = CreateDocument();
        Guid earlierSourceId = Guid.CreateVersion7();
        Guid earlierRawId = Guid.CreateVersion7();
        Guid earlierExcelId = Guid.CreateVersion7();
        Guid newSourceId = Guid.CreateVersion7();
        DateTime now = DateTime.UtcNow;

        submission.ApplyFinalizedFile(document.Id, earlierSourceId, true);
        document.MarkExtractionRequestPublished(Guid.CreateVersion7(), now);
        document.RecordExtractionCompleted(TenantId, document.DocumentCode, Guid.CreateVersion7(), Guid.CreateVersion7(), earlierRawId, earlierExcelId, now);
        PpapDocumentRevision revision = CreateRevision(submission, document, newSourceId, now);

        Guid[] ids = DeleteDocumentRevisionCommandHandler.CollectFileAssetIds(revision, document);

        ids.ShouldBe([newSourceId]);
    }

    [Fact]
    public void CollectFileAssetIds_Should_ReturnEmpty_When_RevisionHasNoFileYet()
    {
        (PpapSubmission submission, PpapSubmissionDocument document) = CreateDocument();
        PpapDocumentRevision revision = PpapDocumentRevision.Create(TenantId, document.Id, 1, submission.OwnerUserId, "pfmea.pdf", ContentType, SizeBytes);

        DeleteDocumentRevisionCommandHandler.CollectFileAssetIds(revision, document).ShouldBeEmpty();
    }

    private static (PpapSubmission Submission, PpapSubmissionDocument Document) CreateDocument()
    {
        PpapSubmission submission = PpapSubmission.CreateDraft(TenantId, Guid.NewGuid(), "Part", "PN-100", "A", 3);
        PpapSubmissionDocument document = submission.AddDocumentSlot("PFMEA");
        return (submission, document);
    }

    private static PpapDocumentRevision CreateRevision(PpapSubmission submission, PpapSubmissionDocument document, Guid sourceId, DateTime now)
    {
        PpapDocumentRevision revision = PpapDocumentRevision.Create(TenantId, document.Id, 1, submission.OwnerUserId, "pfmea.pdf", ContentType, SizeBytes);
        revision.ApplyFinalizedFile(sourceId, ContentType, SizeBytes, true, now);
        return revision;
    }
}
