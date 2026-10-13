using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Credits;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Credits.GetMyCreditBalance;

public static class GetMyCreditBalanceEndpoint
{
    internal static RouteHandlerBuilder MapGetMyCreditBalanceEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/credits/balance", async (IMediator mediator, CancellationToken ct) =>
                Results.Ok(await mediator.Send(new GetMyCreditBalanceQuery(), ct)))
            .WithName("GetMyCreditBalance")
            .WithSummary("Get the current user's credit balance")
            .RequirePermission(PpapPermissions.Credits.View)
            .Produces<CreditBalanceDto>(StatusCodes.Status200OK);
    }
}
