using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Workspace.DecideFindingWaiverRequest;

public sealed class DecideFindingWaiverRequestCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : ICommandHandler<DecideFindingWaiverRequestCommand, Unit>
{
    public async ValueTask<Unit> Handle(DecideFindingWaiverRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid reviewerId = currentUser.GetUserId();
        if (reviewerId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        _ = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        PpapWaiverRequest request = await dbContext.PpapWaiverRequests
            .FirstOrDefaultAsync(x => x.Id == command.RequestId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP waiver request {command.RequestId} not found.");
        request.Decide(command.Approve, reviewerId, command.Reason, command.ExpectedVersion, DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
