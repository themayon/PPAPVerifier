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

namespace DES.Modules.Ppap.Features.v1.Reports.WaivePpapFinding;

public sealed class WaivePpapFindingCommandHandler(PpapDbContext dbContext, ICurrentUser currentUser)
    : ICommandHandler<WaivePpapFindingCommand, Guid>
{
    public async ValueTask<Guid> Handle(WaivePpapFindingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid actorId = currentUser.GetUserId();
        if (actorId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        string tenantId = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        PpapReport report = await dbContext.PpapReports
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == command.ReportId)
            .Join(
                dbContext.PpapJobs.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == PpapJobStatus.Completed),
                x => x.JobId,
                x => x.Id,
                (report, job) => report)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Completed PPAP report {command.ReportId} not found.");
        PpapFindingTargetType targetType = command.FindingKind == PpapFindingKind.Automated
            ? PpapFindingTargetType.Automated
            : PpapFindingTargetType.Manual;
        if (targetType == PpapFindingTargetType.Automated)
        {
            bool exists = await dbContext.PpapFindings.AsNoTracking().AnyAsync(x => x.TenantId == tenantId
                && x.Id == command.FindingId
                && x.JobId == report.JobId
                && x.SubmissionId == report.SubmissionId
                && x.OwnerUserId == report.OwnerUserId,
                cancellationToken).ConfigureAwait(false);
            if (!exists) throw new NotFoundException("The automated finding does not belong to the report.");
        }
        else
        {
            PpapManualFinding manualFinding = await dbContext.PpapManualFindings.AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId
                    && x.Id == command.FindingId
                    && x.ReportId == report.Id
                    && x.JobId == report.JobId
                    && x.SubmissionId == report.SubmissionId
                    && x.OwnerUserId == report.OwnerUserId,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new NotFoundException("The manual finding does not belong to the report.");
            if (manualFinding.CreatedByUserId == actorId) throw new ForbiddenException("A manual-finding creator cannot waive their own finding.");
        }
        bool alreadyActive = await dbContext.PpapFindingWaivers.AnyAsync(x => x.TenantId == tenantId
            && x.TargetType == targetType
            && x.TargetFindingId == command.FindingId
            && x.IsActive,
            cancellationToken).ConfigureAwait(false);
        if (alreadyActive) throw Conflict("The finding already has an active waiver.");

        PpapFindingWaiver waiver = PpapFindingWaiver.Create(
            tenantId,
            report.Id,
            report.SubmissionId,
            report.JobId,
            report.OwnerUserId,
            targetType,
            command.FindingId,
            actorId,
            command.Reason,
            DateTime.UtcNow);
        dbContext.PpapFindingWaivers.Add(waiver);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            throw Conflict("The finding already has an active waiver.");
        }
        return waiver.Id;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
