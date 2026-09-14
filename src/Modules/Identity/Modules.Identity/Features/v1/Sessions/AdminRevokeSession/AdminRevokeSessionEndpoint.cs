using DES.Modules.Identity.Contracts.Authorization;
using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Identity.Contracts.v1.Sessions.AdminRevokeSession;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Identity.Features.v1.Sessions.AdminRevokeSession;

public static class AdminRevokeSessionEndpoint
{
    internal static RouteHandlerBuilder MapAdminRevokeSessionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapDelete("/users/{userId:guid}/sessions/{sessionId:guid}", Handler)
        .WithName("AdminRevokeSession")
        .WithSummary("Revoke a user's session (Admin)")
        .RequirePermission(IdentityPermissions.Sessions.RevokeAll)
        .WithDescription("Revoke a specific session for a user. Requires admin permission.")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<NoContent, NotFound>> Handler(
        Guid userId,
        Guid sessionId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new AdminRevokeSessionCommand(userId, sessionId), cancellationToken);
        return result ? TypedResults.NoContent() : TypedResults.NotFound();
    }
}