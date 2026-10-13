using DES.Modules.Files.Contracts;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Authorization;

public sealed class PpapDocumentFileAccessPolicy(PpapDbContext dbContext) : IFileAccessPolicy
{
    public const string OwnerTypeName = "PpapDocument";

    public string OwnerType => OwnerTypeName;

    public Task<bool> CanAttachAsync(Guid? ownerId, string currentUserId, CancellationToken cancellationToken) =>
        CanAccessAsync(ownerId, currentUserId, requireDraft: true, requireApplicable: true, cancellationToken);

    public Task<bool> CanReadAsync(
        FileAccessContext context,
        string currentUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CanAccessAsync(context.OwnerId, currentUserId, requireDraft: false, requireApplicable: false, cancellationToken);
    }

    public async Task<bool> CanDeleteAsync(
        FileAccessContext context,
        string currentUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!Guid.TryParse(currentUserId, out Guid userId)
            || !Guid.TryParse(context.CreatedByUserId, out Guid uploaderId)
            || userId != uploaderId)
        {
            return false;
        }

        return await CanAccessAsync(
            context.OwnerId,
            currentUserId,
            requireDraft: true,
            requireApplicable: false,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> CanChangeVisibilityAsync(
        FileAccessContext context,
        string currentUserId,
        CancellationToken cancellationToken) => Task.FromResult(false);

    private Task<bool> CanAccessAsync(
        Guid? ownerId,
        string currentUserId,
        bool requireDraft,
        bool requireApplicable,
        CancellationToken cancellationToken)
    {
        if (ownerId is not { } documentId || !Guid.TryParse(currentUserId, out Guid userId))
        {
            return Task.FromResult(false);
        }

        return dbContext.PpapSubmissionDocuments
            .AsNoTracking()
            .AnyAsync(
                document => document.Id == documentId
                    && (!requireApplicable || document.Status != SubmissionDocumentStatus.NotApplicable)
                    && dbContext.PpapSubmissions.Any(submission =>
                        submission.Id == document.SubmissionId
                        && submission.OwnerUserId == userId
                        && (!requireDraft || submission.Status == SubmissionStatus.Draft)),
                cancellationToken);
    }
}
