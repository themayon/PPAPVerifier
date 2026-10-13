using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Domain;
using DES.Modules.Files.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.DocumentIntelligence.Authorization;

public sealed class DocumentExtractionFileAccessPolicy(DocumentIntelligenceDbContext db) : IFileAccessPolicy
{
    public const string OwnerTypeName = "DocumentExtraction";

    public string OwnerType => OwnerTypeName;

    public Task<bool> CanAttachAsync(Guid? ownerId, string currentUserId, CancellationToken cancellationToken) =>
        CanAccessAsync(ownerId, currentUserId, requireNonterminal: true, cancellationToken);

    public Task<bool> CanReadAsync(
        FileAccessContext context,
        string currentUserId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CanAccessAsync(context.OwnerId, currentUserId, requireNonterminal: false, cancellationToken);
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
            requireNonterminal: true,
            cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> CanChangeVisibilityAsync(
        FileAccessContext context,
        string currentUserId,
        CancellationToken cancellationToken) => Task.FromResult(false);

    private Task<bool> CanAccessAsync(
        Guid? ownerId,
        string currentUserId,
        bool requireNonterminal,
        CancellationToken cancellationToken)
    {
        if (ownerId is not { } attemptId || !Guid.TryParse(currentUserId, out Guid userId))
        {
            return Task.FromResult(false);
        }

        return db.ExtractionAttempts
            .AsNoTracking()
            .AnyAsync(
                attempt => attempt.Id == attemptId
                    && attempt.OwnerUserId == userId
                    && (!requireNonterminal
                        || attempt.Status == ExtractionAttemptStatus.Pending
                        || attempt.Status == ExtractionAttemptStatus.Claimed
                        || attempt.Status == ExtractionAttemptStatus.Submitted
                        || attempt.Status == ExtractionAttemptStatus.Processing),
                cancellationToken);
    }
}
