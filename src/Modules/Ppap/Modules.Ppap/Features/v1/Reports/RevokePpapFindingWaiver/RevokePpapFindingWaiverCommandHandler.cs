using System.Data;
using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DES.Modules.Ppap.Features.v1.Reports.RevokePpapFindingWaiver;

public sealed class RevokePpapFindingWaiverCommandHandler(PpapDbContext dbContext, ICurrentUser currentUser)
    : ICommandHandler<RevokePpapFindingWaiverCommand, Guid>
{
    public async ValueTask<Guid> Handle(RevokePpapFindingWaiverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid actorId = currentUser.GetUserId();
        if (actorId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        string tenantId = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        bool completedReportExists = await dbContext.PpapReports
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == command.ReportId)
            .Join(
                dbContext.PpapJobs.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == PpapJobStatus.Completed),
                x => x.JobId,
                x => x.Id,
                (report, job) => report.Id)
            .AnyAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!completedReportExists)
        {
            throw new NotFoundException($"Active waiver {command.WaiverId} for completed report {command.ReportId} not found.");
        }
        PpapFindingWaiver waiver = await dbContext.PpapFindingWaivers
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.Id == command.WaiverId && x.ReportId == command.ReportId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Active waiver {command.WaiverId} for completed report {command.ReportId} not found.");
        if (!waiver.IsActive) throw Conflict("Only an active waiver can be revoked.");
        bool hasRevocation = await dbContext.PpapWaiverRevocations.AnyAsync(x => x.TenantId == tenantId && x.WaiverId == waiver.Id, cancellationToken).ConfigureAwait(false);
        if (hasRevocation) throw Conflict("Only an active waiver can be revoked.");

        waiver.Deactivate();
        PpapWaiverRevocation revocation = PpapWaiverRevocation.Create(tenantId, waiver.Id, actorId, command.Reason, DateTime.UtcNow);
        dbContext.PpapWaiverRevocations.Add(revocation);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Conflict("Only an active waiver can be revoked.");
        }
        catch (DbUpdateException)
        {
            throw Conflict("Only an active waiver can be revoked.");
        }
        return revocation.Id;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
