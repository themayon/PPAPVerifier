using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Tickets.Contracts.Authorization;
using DES.Modules.Tickets.Contracts.v1.Tickets;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Tickets.Features.v1.Tickets.AddTicketComment;

public static class AddTicketCommentEndpoint
{
    public sealed record AddTicketCommentRequest(string Body);

    internal static RouteHandlerBuilder MapAddTicketCommentEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/tickets/{ticketId:guid}/comments",
                async (Guid ticketId, AddTicketCommentRequest body, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(new AddTicketCommentCommand(ticketId, body.Body), ct)))
            .WithName("AddTicketComment")
            .WithSummary("Add a comment to a ticket")
            .RequirePermission(TicketsPermissions.Tickets.Comment)
            .WithIdempotency();
    }
}
