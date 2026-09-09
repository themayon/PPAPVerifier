using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Multitenancy.Contracts.Authorization;
using DES.Modules.Multitenancy.Contracts.Dtos;
using DES.Modules.Multitenancy.Contracts.v1.UpdateTenantTheme;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Multitenancy.Features.v1.UpdateTenantTheme;

public static class UpdateTenantThemeEndpoint
{
    public static RouteHandlerBuilder Map(IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPut("/theme", async (TenantThemeDto theme, IMediator mediator, CancellationToken cancellationToken) =>
            {
                await mediator.Send(new UpdateTenantThemeCommand(theme), cancellationToken);
                return TypedResults.NoContent();
            })
            .WithName("UpdateTenantTheme")
            .WithSummary("Update current tenant theme")
            .WithDescription("Update the theme settings for the current tenant, including colors, typography, and layout.")
            .RequirePermission(MultitenancyPermissions.Tenants.UpdateTheme)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }
}