using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Workspace.GetSubmissionWorkspace;

public sealed class GetSubmissionWorkspaceQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<GetSubmissionWorkspaceQuery, SubmissionWorkspaceDto>
{
    public async ValueTask<SubmissionWorkspaceDto> Handle(
        GetSubmissionWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        _ = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");

        PpapSubmission submission = await dbContext.PpapSubmissions
            .AsNoTracking()
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(
                x => x.Id == query.SubmissionId && x.OwnerUserId == ownerUserId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP submission {query.SubmissionId} not found.");
        Guid[] documentIds = submission.Documents.Select(x => x.Id).ToArray();
        PpapDocumentRevision[] revisions = await dbContext.PpapDocumentRevisions
            .AsNoTracking()
            .Where(x => documentIds.Contains(x.SubmissionDocumentId))
            .OrderByDescending(x => x.RevisionNumber)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        Dictionary<Guid, PpapDocumentRevision[]> byDocument = revisions
            .GroupBy(x => x.SubmissionDocumentId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        WorkingFindingDto[] findings = await dbContext.PpapWorkingFindings
            .AsNoTracking()
            .Where(x => x.SubmissionId == submission.Id && x.IsCurrent)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new WorkingFindingDto(
                x.Id,
                x.ValidationRunId,
                x.RuleVersionId,
                x.SubmissionDocumentId,
                x.DocumentRevisionId,
                x.StableRuleCode,
                x.RuleName,
                x.Status.ToString(),
                x.Message,
                x.InputFingerprint,
                x.EvidenceJson,
                x.CreatedAtUtc))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        WaiverRequestDto[] waiverRequests = await dbContext.PpapWaiverRequests
            .AsNoTracking()
            .Where(x => x.SubmissionId == submission.Id)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => new WaiverRequestDto(
                x.Id,
                x.SubmissionId,
                x.WorkingFindingId,
                x.FindingFingerprint,
                x.RequestedByUserId,
                x.RequestReason,
                x.Status.ToString(),
                x.ReviewedByUserId,
                x.ReviewReason,
                x.RequestedAtUtc,
                x.ReviewedAtUtc,
                x.Version))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        WorkspaceDocumentDto[] documents = submission.Documents
            .OrderBy(x => x.CreatedAtUtc)
            .Select(document =>
            {
                PpapDocumentRevision[] history = byDocument.GetValueOrDefault(document.Id) ?? [];
                return new WorkspaceDocumentDto(
                    document.Id,
                    document.DocumentCode,
                    document.IsRequired,
                    document.Disposition.ToString(),
                    document.Status.ToString(),
                    document.FileAssetId,
                    document.NotApplicableReason,
                    Map(history.FirstOrDefault(x => x.IsCurrent)),
                    history.Select(Map).OfType<DocumentRevisionDto>().ToArray());
            })
            .ToArray();

        return new SubmissionWorkspaceDto(
            submission.Id,
            submission.PartName,
            submission.PartNumber,
            submission.DrawingRevision,
            submission.PpapLevel,
            submission.Status.ToString(),
            submission.Version,
            documents,
            findings,
            waiverRequests);
    }

    private static DocumentRevisionDto? Map(PpapDocumentRevision? revision) => revision is null
        ? null
        : new DocumentRevisionDto(
            revision.Id,
            revision.RevisionNumber,
            revision.Version,
            revision.SourceFileAssetId,
            revision.SourceFileName,
            revision.SourceContentType,
            revision.SourceSizeBytes,
            revision.UploadStatus.ToString(),
            revision.ProcessingStatus.ToString(),
            revision.PreviewPdfFileAssetId,
            revision.GeneratedWorkbookFileAssetId,
            revision.CanonicalDocumentId,
            revision.Provider,
            revision.ExtractorVersion,
            revision.FailureCode,
            revision.CreatedAtUtc,
            revision.UpdatedAtUtc,
            revision.CompletedAtUtc);
}
