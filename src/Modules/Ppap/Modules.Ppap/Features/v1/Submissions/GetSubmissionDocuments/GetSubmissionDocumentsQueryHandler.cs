using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Submissions.GetSubmissionDocuments;

public sealed class ListSubmissionDocumentsQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<ListSubmissionDocumentsQuery, IReadOnlyList<SubmissionDocumentDto>>
{
    public async ValueTask<IReadOnlyList<SubmissionDocumentDto>> Handle(
        ListSubmissionDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        _ = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        bool ownsSubmission = await dbContext.PpapSubmissions
            .AsNoTracking()
            .AnyAsync(x => x.Id == query.SubmissionId && x.OwnerUserId == ownerUserId, cancellationToken)
            .ConfigureAwait(false);
        if (!ownsSubmission)
        {
            throw new NotFoundException($"PPAP submission {query.SubmissionId} not found.");
        }

        return await dbContext.PpapSubmissionDocuments
            .AsNoTracking()
            .Where(x => x.SubmissionId == query.SubmissionId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new SubmissionDocumentDto(
                x.Id,
                x.SubmissionId,
                x.DocumentCode,
                x.IsRequired,
                x.Disposition,
                x.Status.ToString(),
                x.FileAssetId,
                x.NotApplicableReason,
                x.CreatedAtUtc,
                x.UpdatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
