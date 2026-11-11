using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DES.Modules.Ppap.Features.v1.Submissions.DeleteDocumentRevision;

public sealed class DeleteDocumentRevisionCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : ICommandHandler<DeleteDocumentRevisionCommand, Unit>
{
    public async ValueTask<Unit> Handle(DeleteDocumentRevisionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");

        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        PpapSubmission submission = await dbContext.PpapSubmissions
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(x => x.Id == command.SubmissionId && x.OwnerUserId == ownerUserId, cancellationToken)
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

        PpapDocumentRevision revision = await dbContext.PpapDocumentRevisions
            .FirstOrDefaultAsync(x => x.Id == command.RevisionId && x.SubmissionDocumentId == command.DocumentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP document revision {command.RevisionId} not found.");
        if (!revision.IsCurrent)
        {
            throw Conflict("Only the current document revision can be deleted.");
        }

        DateTime utcNow = DateTime.UtcNow;
        PpapWorkingFinding[] findings = await dbContext.PpapWorkingFindings
            .Where(x => x.SubmissionId == submission.Id && x.SubmissionDocumentId == command.DocumentId && x.IsCurrent)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (PpapWorkingFinding finding in findings) finding.Supersede(utcNow);

        submission.RemoveDocumentRevision(command.DocumentId);
        dbContext.PpapDocumentRevisions.Remove(revision);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
