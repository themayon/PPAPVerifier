using Finbuckle.MultiTenant.Abstractions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Multitenancy.Contracts.Authorization;
using DES.Modules.Multitenancy.Contracts;
using DES.Modules.Multitenancy.Contracts.v1.ResetTenantTheme;
using Mediator;

namespace DES.Modules.Multitenancy.Features.v1.ResetTenantTheme;

public sealed class ResetTenantThemeCommandHandler(
    ITenantThemeService themeService,
    IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor)
    : ICommandHandler<ResetTenantThemeCommand>
{
    public async ValueTask<Unit> Handle(ResetTenantThemeCommand command, CancellationToken cancellationToken)
    {
        var tenantId = tenantAccessor.MultiTenantContext?.TenantInfo?.Id
            ?? throw new InvalidOperationException("No tenant context available");

        await themeService.ResetThemeAsync(tenantId, cancellationToken);

        return Unit.Value;
    }
}