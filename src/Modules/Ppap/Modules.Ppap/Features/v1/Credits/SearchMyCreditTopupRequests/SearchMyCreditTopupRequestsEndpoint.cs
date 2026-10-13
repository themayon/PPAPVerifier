using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Credits;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Credits.SearchMyCreditTopupRequests;

public static class SearchMyCreditTopupRequestsEndpoint
{
    internal static RouteHandlerBuilder MapSearchMyCreditTopupRequestsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/credits/topup-requests/me", async (
                string? status,
                int? page,
                int? pageSize,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                SearchMyCreditTopupRequestsQuery query = new()
                {
                    Status = status,
                    PageNumber = page ?? 1,
                    PageSize = pageSize ?? 20
                };
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .WithName("SearchMyCreditTopupRequests")
            .WithSummary("Search my credit top-up requests")
            .RequirePermission(PpapPermissions.Credits.View)
            .Produces<PagedResponse<CreditTopupRequestDto>>(StatusCodes.Status200OK);
    }
}
