using System.Diagnostics;
using DES.Framework.Eventing.Abstractions;
using DES.Modules.Files.Contracts.Events;
using DES.Modules.Ppap.Authorization;
using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DES.Modules.Ppap.IntegrationEventHandlers;

public sealed class PpapFileFinalizedHandler(
    PpapDbContext dbContext,
    IOutboxWriter outbox) : IIntegrationEventHandler<FileFinalizedIntegrationEvent>
{
    public async Task HandleAsync(FileFinalizedIntegrationEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event.FinalStatus is not 1 and not 2 || @event.OwnerId is not { } ownerId) return;

        if (string.Equals(@event.OwnerType, PpapFileOwnerTypes.DocumentRevision, StringComparison.Ordinal))
        {
            await HandleRevisionAsync(@event, ownerId, ct).ConfigureAwait(false);
            return;
        }
        if (!string.Equals(@event.OwnerType, PpapDocumentFileAccessPolicy.OwnerTypeName, StringComparison.Ordinal)) return;

        PpapSubmission? submission = await dbContext.PpapSubmissions
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Documents.Any(document => document.Id == ownerId), ct)
            .ConfigureAwait(false);
        if (submission is null || submission.Status != SubmissionStatus.Draft) return;

        submission.ApplyFinalizedFile(ownerId, @event.FileAssetId, @event.FinalStatus == 1);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private async Task HandleRevisionAsync(
        FileFinalizedIntegrationEvent @event,
        Guid revisionId,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        var data = await dbContext.PpapDocumentRevisions
            .Where(revision => revision.Id == revisionId)
            .Join(
                dbContext.PpapSubmissionDocuments,
                revision => revision.SubmissionDocumentId,
                document => document.Id,
                (revision, document) => new { Revision = revision, Document = document })
            .Join(
                dbContext.PpapSubmissions,
                item => item.Document.SubmissionId,
                submission => submission.Id,
                (item, submission) => new { item.Revision, item.Document, Submission = submission })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (data is null || data.Submission.Status != SubmissionStatus.Draft)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        bool available = @event.FinalStatus == 1;
        data.Document.ApplyCurrentRevisionFile(@event.FileAssetId, available);
        data.Revision.ApplyFinalizedFile(
            @event.FileAssetId,
            @event.ContentType,
            @event.SizeBytes,
            available,
            @event.OccurredOnUtc);
        if (available)
        {
            Guid eventId = data.Revision.ExtractionRequestEventId ?? Guid.CreateVersion7();
            _ = data.Document.MarkExtractionRequestPublished(eventId, @event.OccurredOnUtc);
            if (data.Revision.QueueExtraction(eventId, @event.OccurredOnUtc))
            {
                await outbox.AddAsync(new PpapDocumentRevisionExtractionRequestedIntegrationEvent(
                    eventId,
                    @event.OccurredOnUtc,
                    data.Submission.TenantId,
                    string.IsNullOrWhiteSpace(@event.CorrelationId)
                        ? Activity.Current?.Id ?? eventId.ToString()
                        : @event.CorrelationId,
                    "Ppap",
                    data.Submission.Id,
                    data.Document.Id,
                    data.Revision.Id,
                    data.Submission.OwnerUserId,
                    @event.FileAssetId,
                    data.Document.DocumentCode), cancellationToken).ConfigureAwait(false);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
