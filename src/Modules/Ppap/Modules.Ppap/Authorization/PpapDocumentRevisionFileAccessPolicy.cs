using DES.Modules.Files.Contracts;
using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Authorization;

public sealed class PpapDocumentRevisionFileAccessPolicy(PpapDbContext dbContext) : IFileAccessPolicy
{
    public string OwnerType => PpapFileOwnerTypes.DocumentRevision;

    public Task<bool> CanAttachAsync(Guid? ownerId, string currentUserId, CancellationToken cancellationToken) =>
        CanAccessAsync(ownerId, currentUserId, requireDraft: true, requirePendingUpload: true, cancellationToken);

    public Task<bool> CanReadAsync(FileAccessContext context, string currentUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CanAccessAsync(context.OwnerId, currentUserId, requireDraft: false, requirePendingUpload: false, cancellationToken);
    }

    public Task<bool> CanDeleteAsync(FileAccessContext context, string currentUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CanAccessAsync(context.OwnerId, currentUserId, requireDraft: true, requirePendingUpload: true, cancellationToken);
    }

    public Task<bool> CanChangeVisibilityAsync(FileAccessContext context, string currentUserId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    private Task<bool> CanAccessAsync(
        Guid? ownerId,
        string currentUserId,
        bool requireDraft,
        bool requirePendingUpload,
        CancellationToken cancellationToken)
    {
        if (ownerId is not { } revisionId || !Guid.TryParse(currentUserId, out Guid userId))
        {
            return Task.FromResult(false);
        }

        return dbContext.PpapDocumentRevisions
            .AsNoTracking()
            .AnyAsync(
                revision => revision.Id == revisionId
                    && revision.OwnerUserId == userId
                    && revision.IsCurrent
                    && (!requirePendingUpload
                        || (revision.UploadStatus == DocumentUploadStatus.Uploading
                            && revision.SourceFileAssetId == null))
                    && dbContext.PpapSubmissionDocuments.Any(document =>
                        document.Id == revision.SubmissionDocumentId
                        && dbContext.PpapSubmissions.Any(submission =>
                            submission.Id == document.SubmissionId
                            && submission.OwnerUserId == userId
                            && (!requireDraft || submission.Status == SubmissionStatus.Draft))),
                cancellationToken);
    }
}
