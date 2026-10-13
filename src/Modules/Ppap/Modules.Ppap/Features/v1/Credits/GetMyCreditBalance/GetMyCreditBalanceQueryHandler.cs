using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Credits.GetMyCreditBalance;

public sealed class GetMyCreditBalanceQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<GetMyCreditBalanceQuery, CreditBalanceDto>
{
    public async ValueTask<CreditBalanceDto> Handle(
        GetMyCreditBalanceQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        CreditBalanceDto? balance = await dbContext.CreditAccounts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.OwnerUserId == ownerUserId)
            .Select(x => new CreditBalanceDto(x.AvailableCredits, x.ReservedCredits))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return balance ?? new CreditBalanceDto(0, 0);
    }
}
