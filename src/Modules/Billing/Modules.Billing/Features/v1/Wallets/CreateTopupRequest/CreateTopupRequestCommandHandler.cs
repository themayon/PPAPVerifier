using Finbuckle.MultiTenant.Abstractions;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Billing.Contracts.v1.Wallets;
using DES.Modules.Billing.Data;
using DES.Modules.Billing.Domain;
using Mediator;

namespace DES.Modules.Billing.Features.v1.Wallets.CreateTopupRequest;

public sealed class CreateTopupRequestCommandHandler(
    BillingDbContext db,
    IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor,
    ICurrentUser currentUser)
    : ICommandHandler<CreateTopupRequestCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateTopupRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // BillingDbContext is not tenant-filtered; resolve caller's own tenant and scope strictly to it.
        var tenantId = tenantAccessor.MultiTenantContext?.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        var requestedBy = currentUser.IsAuthenticated() ? currentUser.GetUserId().ToString() : null;
        var request = TopupRequest.Create(tenantId, command.Amount, "USD", command.Note, requestedBy);
        db.TopupRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return request.Id;
    }
}
