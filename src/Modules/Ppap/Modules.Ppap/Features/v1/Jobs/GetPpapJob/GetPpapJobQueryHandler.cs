using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Jobs;
using DES.Modules.Ppap.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Jobs.GetPpapJob;

public sealed class GetPpapJobQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<GetPpapJobQuery, PpapJobDto>
{
    public async ValueTask<PpapJobDto> Handle(
        GetPpapJobQuery query,
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

        return await dbContext.PpapJobs
            .AsNoTracking()
            .Where(x => x.Id == query.Id && x.OwnerUserId == ownerUserId)
            .Select(x => new PpapJobDto(
                x.Id,
                x.SubmissionId,
                x.SubmissionVersion,
                x.Status.ToString(),
                x.CurrentStage.ToString(),
                x.ProgressPercent,
                x.CreatedAtUtc,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.FailedAtUtc,
                x.ErrorCode,
                x.ErrorMessage,
                dbContext.PpapReports
                    .Where(report => report.JobId == x.Id)
                    .Select(report => (Guid?)report.Id)
                    .FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP job {query.Id} not found.");
    }
}
