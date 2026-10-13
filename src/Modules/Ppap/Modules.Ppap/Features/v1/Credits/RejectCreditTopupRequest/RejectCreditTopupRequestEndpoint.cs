using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Credits;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Credits.RejectCreditTopupRequest;

public static class RejectCreditTopupRequestEndpoint
{
    public sealed record RejectCreditTopupRequestBody(string? DecisionNote);

    internal static RouteHandlerBuilder MapRejectCreditTopupRequestEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/credits/topup-requests/{id:guid}/reject", async (
                Guid id,
                RejectCreditTopupRequestBody body,
                IMediator mediator,
                CancellationToken ct) =>
            {
                await mediator.Send(new RejectCreditTopupRequestCommand(id, body.DecisionNote), ct);
                return Results.NoContent();
            })
            .WithName("RejectCreditTopupRequest")
            .WithSummary("Reject a credit top-up request")
            .RequirePermission(PpapPermissions.Credits.Manage)
            .WithIdempotency()
            .Produces(StatusCodes.Status204NoContent);
    }
}
