using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Operations;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Operations.GetPpapOperationsSummary;

public sealed class GetPpapOperationsSummaryQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<GetPpapOperationsSummaryQuery, PpapOperationsSummaryDto>
{
    public async ValueTask<PpapOperationsSummaryDto> Handle(
        GetPpapOperationsSummaryQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (currentUser.GetUserId() == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }

        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        long totalSubmissions = await dbContext.PpapSubmissions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);
        long queuedJobs = await CountJobs(PpapJobStatus.Queued, tenantId, cancellationToken).ConfigureAwait(false);
        long processingJobs = await CountJobs(PpapJobStatus.Processing, tenantId, cancellationToken).ConfigureAwait(false);
        long completedJobs = await CountJobs(PpapJobStatus.Completed, tenantId, cancellationToken).ConfigureAwait(false);
        long failedJobs = await CountJobs(PpapJobStatus.Failed, tenantId, cancellationToken).ConfigureAwait(false);
        long pendingTopupRequests = await dbContext.CreditTopupRequests
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == CreditTopupRequestStatus.Pending)
            .LongCountAsync(cancellationToken)
            .ConfigureAwait(false);
        long totalAvailableCredits = await dbContext.CreditAccounts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .SumAsync(x => (long)x.AvailableCredits, cancellationToken)
            .ConfigureAwait(false);
        long totalReservedCredits = await dbContext.CreditAccounts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .SumAsync(x => (long)x.ReservedCredits, cancellationToken)
            .ConfigureAwait(false);

        return new PpapOperationsSummaryDto(
            totalSubmissions,
            queuedJobs,
            processingJobs,
            completedJobs,
            failedJobs,
            pendingTopupRequests,
            totalAvailableCredits,
            totalReservedCredits);
    }

    private Task<long> CountJobs(PpapJobStatus status, string tenantId, CancellationToken cancellationToken) =>
        dbContext.PpapJobs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == status)
            .LongCountAsync(cancellationToken);
}
