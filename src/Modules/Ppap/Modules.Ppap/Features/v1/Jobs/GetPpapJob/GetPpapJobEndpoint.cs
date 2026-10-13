using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Jobs;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Jobs.GetPpapJob;

public static class GetPpapJobEndpoint
{
    internal static RouteHandlerBuilder MapGetPpapJobEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/jobs/{id:guid}", async (
                Guid id,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetPpapJobQuery(id), cancellationToken)))
            .WithName("GetPpapJob")
            .WithSummary("Get a PPAP processing job")
            .RequirePermission(PpapPermissions.Submissions.View)
            .Produces<PpapJobDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
