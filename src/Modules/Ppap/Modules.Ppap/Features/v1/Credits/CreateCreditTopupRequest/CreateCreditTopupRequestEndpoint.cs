using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Credits;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Credits.CreateCreditTopupRequest;

public static class CreateCreditTopupRequestEndpoint
{
    internal static RouteHandlerBuilder MapCreateCreditTopupRequestEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/credits/topup-requests", async (
                CreateCreditTopupRequestCommand command,
                IMediator mediator,
                CancellationToken ct) =>
            {
                Guid id = await mediator.Send(command, ct);
                return Results.Created($"/api/v1/ppap/credits/topup-requests/{id}", id);
            })
            .WithName("CreateCreditTopupRequest")
            .WithSummary("Request a credit top-up")
            .RequirePermission(PpapPermissions.Credits.RequestTopup)
            .WithIdempotency()
            .Produces<Guid>(StatusCodes.Status201Created);
    }
}
