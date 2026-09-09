using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Chat.Contracts.Authorization;
using DES.Modules.Chat.Contracts.v1.Commands;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Chat.Features.v1.Channels.AddChannelMembers;

public static class AddChannelMembersEndpoint
{
    internal static RouteHandlerBuilder MapAddChannelMembersEndpoint(this IEndpointRouteBuilder endpoints)
        => endpoints.MapPost("/channels/{id:guid}/members",
                async (Guid id, [FromBody] AddMembersBody body, IMediator mediator, CancellationToken cancellationToken) =>
                {
                    await mediator.Send(new AddChannelMembersCommand(id, body.UserIds), cancellationToken);
                    return Results.NoContent();
                })
            .WithName("AddChannelMembers")
            .WithSummary("Invite users to a channel")
            .RequirePermission(ChatPermissions.Channels.View);

    public sealed record AddMembersBody(IReadOnlyList<string> UserIds);
}
