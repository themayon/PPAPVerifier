using DES.Modules.Identity.Contracts.Authorization;
using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Identity.Contracts.DTOs;
using DES.Modules.Identity.Contracts.v1.Users.GetUsers;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Identity.Features.v1.Users.GetUsers;

public static class GetUsersListEndpoint
{
    internal static RouteHandlerBuilder MapGetUsersListEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/users", async (CancellationToken cancellationToken, IMediator mediator) =>
            TypedResults.Ok(await mediator.Send(new GetUsersQuery(), cancellationToken)))
        .WithName("ListUsers")
        .WithSummary("List users")
        .RequirePermission(IdentityPermissions.Users.View)
        .WithDescription("Retrieve a list of users for the current tenant.")
        .Produces<IEnumerable<UserDto>>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);
    }
}