using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Multitenancy.Contracts.Authorization;
using DES.Modules.Multitenancy.Contracts.Dtos;
using DES.Modules.Multitenancy.Contracts.v1.TenantProvisioning;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Multitenancy.Features.v1.TenantProvisioning.GetTenantProvisioningStatus;

public static class GetTenantProvisioningStatusEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/{tenantId}/provisioning", async (
            [FromRoute] string tenantId,
            [FromServices] IMediator mediator,
            CancellationToken cancellationToken) =>
            TypedResults.Ok(await mediator.Send(new GetTenantProvisioningStatusQuery(tenantId), cancellationToken)))
            .WithName("GetTenantProvisioningStatus")
            .WithSummary("Get tenant provisioning status")
            .RequirePermission(MultitenancyPermissions.Tenants.View)
            .WithDescription("Get latest provisioning status for a tenant.")
            .Produces<TenantProvisioningStatusDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }
}