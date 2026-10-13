using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Credits;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Credits.ApproveCreditTopupRequest;

public static class ApproveCreditTopupRequestEndpoint
{
    public sealed record ApproveCreditTopupRequestBody(string? DecisionNote);

    internal static RouteHandlerBuilder MapApproveCreditTopupRequestEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/credits/topup-requests/{id:guid}/approve", async (
                Guid id,
                ApproveCreditTopupRequestBody body,
                IMediator mediator,
                CancellationToken ct) =>
            {
                await mediator.Send(new ApproveCreditTopupRequestCommand(id, body.DecisionNote), ct);
                return Results.NoContent();
            })
            .WithName("ApproveCreditTopupRequest")
            .WithSummary("Approve a credit top-up request")
            .RequirePermission(PpapPermissions.Credits.Manage)
            .WithIdempotency()
            .Produces(StatusCodes.Status204NoContent);
    }
}
