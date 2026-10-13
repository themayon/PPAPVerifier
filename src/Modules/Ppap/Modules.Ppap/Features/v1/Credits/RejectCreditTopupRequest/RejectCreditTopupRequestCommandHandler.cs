using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Credits.RejectCreditTopupRequest;

public sealed class RejectCreditTopupRequestCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : ICommandHandler<RejectCreditTopupRequestCommand, Unit>
{
    public async ValueTask<Unit> Handle(RejectCreditTopupRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid decisionByUserId = currentUser.GetUserId();
        if (decisionByUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        CreditTopupRequest request = await dbContext.CreditTopupRequests
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Credit top-up request {command.Id} not found.");

        request.Reject(decisionByUserId, command.DecisionNote);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
