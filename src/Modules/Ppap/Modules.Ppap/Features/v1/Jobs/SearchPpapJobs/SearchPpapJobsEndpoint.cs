using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Jobs;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Jobs.SearchPpapJobs;

public static class SearchPpapJobsEndpoint
{
    internal static RouteHandlerBuilder MapSearchPpapJobsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/operations/jobs", async (
                string? status,
                string? search,
                int? pageNumber,
                int? pageSize,
                string? sort,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                SearchPpapJobsQuery query = new()
                {
                    Status = status,
                    Search = search,
                    PageNumber = pageNumber ?? 1,
                    PageSize = pageSize ?? 20,
                    Sort = sort
                };
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .WithName("SearchPpapJobs")
            .WithSummary("Search tenant PPAP processing jobs")
            .RequirePermission(PpapPermissions.Jobs.View)
            .Produces<PagedResponse<PpapAdminJobDto>>(StatusCodes.Status200OK);
    }
}
