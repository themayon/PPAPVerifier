using Finbuckle.MultiTenant.Abstractions;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Billing.Contracts.Dtos;
using DES.Modules.Billing.Contracts.v1.Wallets;
using DES.Modules.Billing.Mappings;
using DES.Modules.Billing.Services;
using Mediator;

namespace DES.Modules.Billing.Features.v1.Wallets.GetMyWallet;

public sealed class GetMyWalletQueryHandler(
    IBillingService billingService,
    IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor)
    : IQueryHandler<GetMyWalletQuery, WalletDto>
{
    public async ValueTask<WalletDto> Handle(GetMyWalletQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // BillingDbContext is not tenant-filtered; resolve caller's own tenant and scope strictly to it.
        var tenantId = tenantAccessor.MultiTenantContext?.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        var wallet = await billingService.GetOrCreateWalletAsync(tenantId, "USD", cancellationToken).ConfigureAwait(false);
        return wallet.ToDto();
    }
}
