using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Workspace.RequestFindingWaiver;

public sealed class RequestFindingWaiverCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : ICommandHandler<RequestFindingWaiverCommand, Guid>
{
    public async ValueTask<Guid> Handle(RequestFindingWaiverCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid requesterId = currentUser.GetUserId();
        if (requesterId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        string tenantId = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        bool ownsSubmission = await dbContext.PpapSubmissions.AnyAsync(
            x => x.Id == command.SubmissionId && x.OwnerUserId == requesterId,
            cancellationToken).ConfigureAwait(false);
        if (!ownsSubmission) throw new NotFoundException($"PPAP submission {command.SubmissionId} not found.");
        PpapWorkingFinding finding = await dbContext.PpapWorkingFindings
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.Id == command.WorkingFindingId && x.SubmissionId == command.SubmissionId && x.IsCurrent,
                cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Current PPAP finding {command.WorkingFindingId} not found.");
        bool pendingExists = await dbContext.PpapWaiverRequests.AnyAsync(
            x => x.WorkingFindingId == finding.Id && x.Status == WaiverRequestStatus.Pending,
            cancellationToken).ConfigureAwait(false);
        if (pendingExists) throw Conflict("A pending waiver request already exists for this finding.");

        PpapWaiverRequest request = PpapWaiverRequest.Create(
            tenantId,
            command.SubmissionId,
            finding.Id,
            finding.InputFingerprint,
            requesterId,
            command.Reason,
            DateTime.UtcNow);
        dbContext.PpapWaiverRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return request.Id;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
