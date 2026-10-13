using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Operations;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Operations.GetPpapOperationsSummary;

public static class GetPpapOperationsSummaryEndpoint
{
    internal static RouteHandlerBuilder MapGetPpapOperationsSummaryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/operations/summary", async (
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetPpapOperationsSummaryQuery(), cancellationToken)))
            .WithName("GetPpapOperationsSummary")
            .WithSummary("Get tenant PPAP operations summary")
            .RequirePermission(PpapPermissions.Operations.View)
            .Produces<PpapOperationsSummaryDto>(StatusCodes.Status200OK);
    }
}
