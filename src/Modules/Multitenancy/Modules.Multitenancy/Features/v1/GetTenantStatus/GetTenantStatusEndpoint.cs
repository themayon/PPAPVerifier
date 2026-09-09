using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Multitenancy.Contracts.Authorization;
using DES.Modules.Multitenancy.Contracts.Dtos;
using DES.Modules.Multitenancy.Contracts.v1.GetTenantStatus;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Multitenancy.Features.v1.GetTenantStatus;

public static class GetTenantStatusEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/{id}/status", async (string id, IMediator mediator, CancellationToken cancellationToken) =>
                TypedResults.Ok(await mediator.Send(new GetTenantStatusQuery(id), cancellationToken)))
            .WithName("GetTenantStatus")
            .WithSummary("Get tenant status")
            .WithDescription("Retrieve status information for a tenant, including activation, validity, and basic metadata.")
            .RequirePermission(MultitenancyPermissions.Tenants.View)
            .Produces<TenantStatusDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);
    }
}