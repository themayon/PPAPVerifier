using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Workspace.GetPendingWaiverRequests;

public sealed class GetPendingWaiverRequestsQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<GetPendingWaiverRequestsQuery, IReadOnlyList<WaiverReviewDto>>
{
    public async ValueTask<IReadOnlyList<WaiverReviewDto>> Handle(
        GetPendingWaiverRequestsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (currentUser.GetUserId() == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        _ = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        bool submissionExists = await dbContext.PpapSubmissions.AnyAsync(x => x.Id == query.SubmissionId, cancellationToken).ConfigureAwait(false);
        if (!submissionExists) throw new NotFoundException($"PPAP submission {query.SubmissionId} not found.");

        return await dbContext.PpapWaiverRequests
            .AsNoTracking()
            .Where(request => request.SubmissionId == query.SubmissionId && request.Status == WaiverRequestStatus.Pending)
            .Join(
                dbContext.PpapWorkingFindings.AsNoTracking(),
                request => request.WorkingFindingId,
                finding => finding.Id,
                (request, finding) => new WaiverReviewDto(
                    new WaiverRequestDto(
                        request.Id,
                        request.SubmissionId,
                        request.WorkingFindingId,
                        request.FindingFingerprint,
                        request.RequestedByUserId,
                        request.RequestReason,
                        request.Status.ToString(),
                        request.ReviewedByUserId,
                        request.ReviewReason,
                        request.RequestedAtUtc,
                        request.ReviewedAtUtc,
                        request.Version),
                    finding.StableRuleCode,
                    finding.RuleName,
                    finding.Message,
                    finding.EvidenceJson))
            .OrderBy(x => x.Request.RequestedAtUtc)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
