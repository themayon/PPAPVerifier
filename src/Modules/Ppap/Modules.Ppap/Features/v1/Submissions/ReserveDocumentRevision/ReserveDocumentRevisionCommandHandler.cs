using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DES.Modules.Ppap.Features.v1.Submissions.ReserveDocumentRevision;

public sealed class ReserveDocumentRevisionCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : ICommandHandler<ReserveDocumentRevisionCommand, Guid>
{
    public async ValueTask<Guid> Handle(
        ReserveDocumentRevisionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        string tenantId = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");

        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        PpapSubmission submission = await dbContext.PpapSubmissions
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(
                x => x.Id == command.SubmissionId && x.OwnerUserId == ownerUserId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP submission {command.SubmissionId} not found.");
        if (submission.Version != command.ExpectedVersion)
        {
            throw Conflict($"PPAP submission version conflict. Expected {command.ExpectedVersion}, current {submission.Version}.");
        }
        if (!submission.Documents.Any(x => x.Id == command.DocumentId))
        {
            throw new NotFoundException($"PPAP document {command.DocumentId} not found.");
        }

        PpapDocumentRevision[] revisions = await dbContext.PpapDocumentRevisions
            .Where(x => x.SubmissionDocumentId == command.DocumentId)
            .OrderBy(x => x.RevisionNumber)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        DateTime utcNow = DateTime.UtcNow;
        PpapDocumentRevision? current = revisions.SingleOrDefault(x => x.IsCurrent);
        current?.Supersede(utcNow);
        int revisionNumber = revisions.Length == 0 ? 1 : checked(revisions.Max(x => x.RevisionNumber) + 1);
        PpapDocumentRevision revision = PpapDocumentRevision.Create(
            tenantId,
            command.DocumentId,
            revisionNumber,
            ownerUserId,
            command.FileName,
            command.ContentType,
            command.SizeBytes);
        dbContext.PpapDocumentRevisions.Add(revision);
        submission.RecordDocumentRevisionReserved(command.DocumentId);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return revision.Id;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
