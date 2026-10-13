using DES.Framework.Eventing.Abstractions;
using DES.Modules.Files.Contracts.Events;
using DES.Modules.Ppap.Authorization;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.IntegrationEventHandlers;

public sealed class PpapFileFinalizedHandler(PpapDbContext dbContext)
    : IIntegrationEventHandler<FileFinalizedIntegrationEvent>
{
    public async Task HandleAsync(FileFinalizedIntegrationEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (!string.Equals(@event.OwnerType, PpapDocumentFileAccessPolicy.OwnerTypeName, StringComparison.Ordinal)
            || @event.OwnerId is not { } documentId
            || @event.FinalStatus is not 1 and not 2)
        {
            return;
        }

        PpapSubmission? submission = await dbContext.PpapSubmissions
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Documents.Any(document => document.Id == documentId), ct)
            .ConfigureAwait(false);
        if (submission is null || submission.Status != SubmissionStatus.Draft)
        {
            return;
        }

        submission.ApplyFinalizedFile(documentId, @event.FileAssetId, @event.FinalStatus == 1);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
