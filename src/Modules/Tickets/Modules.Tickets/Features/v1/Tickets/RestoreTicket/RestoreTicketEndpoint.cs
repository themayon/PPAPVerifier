using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Tickets.Contracts.Authorization;
using DES.Modules.Tickets.Contracts.v1.Tickets;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Tickets.Features.v1.Tickets.RestoreTicket;

public static class RestoreTicketEndpoint
{
    internal static RouteHandlerBuilder MapRestoreTicketEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/tickets/{ticketId:guid}/restore",
                async (Guid ticketId, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(new RestoreTicketCommand(ticketId), ct)))
            .WithName("RestoreTicket")
            .WithSummary("Restore a soft-deleted ticket")
            .RequirePermission(TicketsPermissions.Tickets.Restore)
            .WithIdempotency();
    }
}
