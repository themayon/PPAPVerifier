using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Files.Contracts;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace DES.Modules.Ppap.Features.v1.Submissions.DeleteDocumentRevision;

public sealed partial class DeleteDocumentRevisionCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser,
    IInternalFileArtifactService fileArtifacts,
    ILogger<DeleteDocumentRevisionCommandHandler> logger) : ICommandHandler<DeleteDocumentRevisionCommand, Unit>
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

        PpapSubmissionDocument document = submission.Documents.Single(x => x.Id == command.DocumentId);
        Guid[] fileAssetIds = CollectFileAssetIds(revision, document);
        submission.RemoveDocumentRevision(command.DocumentId);
        dbContext.PpapDocumentRevisions.Remove(revision);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await fileArtifacts.SoftDeleteAsync(fileAssetIds, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogFileCleanupFailed(logger, exception, command.RevisionId, fileAssetIds.Length);
        }
        return Unit.Value;
    }

    internal static Guid[] CollectFileAssetIds(PpapDocumentRevision revision, PpapSubmissionDocument document)
    {
        bool sameSource = document.FileAssetId is not null && document.FileAssetId == revision.SourceFileAssetId;
        bool sameAttempt = sameSource
            && document.ExtractionAttemptId is not null
            && document.ExtractionAttemptId == revision.ExtractionAttemptId;
        Guid?[] candidates =
        [
            revision.SourceFileAssetId,
            revision.RawResponseFileAssetId,
            revision.GeneratedWorkbookFileAssetId,
            revision.PreviewPdfFileAssetId,
            sameSource ? document.FileAssetId : null,
            sameAttempt ? document.RawResponseFileAssetId : null,
            sameAttempt ? document.GeneratedExcelFileAssetId : null,
        ];
        return candidates
            .Where(id => id is Guid value && value != Guid.Empty)
            .Select(id => id!.Value)
            .Distinct()
            .ToArray();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to soft-delete {FileCount} file assets for deleted PPAP revision {RevisionId}; they remain until manually purged")]
    private static partial void LogFileCleanupFailed(ILogger logger, Exception exception, Guid revisionId, int fileCount);

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
