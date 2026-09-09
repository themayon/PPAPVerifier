using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Identity.Contracts.Services;
using DES.Modules.Identity.Contracts.v1.Impersonation;
using DES.Modules.Identity.Contracts.v1.Impersonation.GetImpersonationGrants;
using Mediator;

namespace DES.Modules.Identity.Features.v1.Impersonation.GetImpersonationGrants;

public sealed class GetImpersonationGrantsQueryHandler(
    IImpersonationGrantService grantService,
    ICurrentUser currentUser)
    : IQueryHandler<GetImpersonationGrantsQuery, IReadOnlyList<ImpersonationGrantDto>>
{
    public async ValueTask<IReadOnlyList<ImpersonationGrantDto>> Handle(
        GetImpersonationGrantsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var callerTenant = currentUser.GetTenant()
            ?? throw new UnauthorizedException("missing tenant context");
        var isRoot = string.Equals(callerTenant, MultitenancyConstants.Root.Id, StringComparison.Ordinal);

        // Tenant scoping: root operators target any tenant; tenant admins are locked to their
        // own regardless of input. Mirrors the StartImpersonation cross-tenant rule.
        var tenantFilter = isRoot ? request.ImpersonatedTenantId : callerTenant;

        return await grantService.ListAsync(
            status: request.Status,
            impersonatedTenantId: tenantFilter,
            actorUserId: request.ActorUserId,
            take: request.Take,
            ct: cancellationToken).ConfigureAwait(false);
    }
}
