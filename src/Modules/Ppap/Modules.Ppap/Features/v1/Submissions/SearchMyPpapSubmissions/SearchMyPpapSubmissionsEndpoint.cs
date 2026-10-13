using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.SearchMyPpapSubmissions;

public static class SearchMyPpapSubmissionsEndpoint
{
    internal static RouteHandlerBuilder MapSearchMyPpapSubmissionsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/submissions", async (
                string? search,
                string? status,
                int? pageNumber,
                int? pageSize,
                string? sort,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                SearchMyPpapSubmissionsQuery query = new()
                {
                    Search = search,
                    Status = status,
                    PageNumber = pageNumber ?? 1,
                    PageSize = pageSize ?? 20,
                    Sort = sort
                };
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .WithName("SearchMyPpapSubmissions")
            .WithSummary("Search my PPAP submissions")
            .RequirePermission(PpapPermissions.Submissions.View)
            .Produces<PagedResponse<PpapSubmissionDto>>(StatusCodes.Status200OK);
    }
}
