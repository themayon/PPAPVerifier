using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Credits;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Credits.SearchCreditTopupRequests;

public static class SearchCreditTopupRequestsEndpoint
{
    internal static RouteHandlerBuilder MapSearchCreditTopupRequestsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/credits/topup-requests", async (
                string? status,
                int? pageNumber,
                int? pageSize,
                string? sort,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                SearchCreditTopupRequestsQuery query = new()
                {
                    Status = status,
                    PageNumber = pageNumber ?? 1,
                    PageSize = pageSize ?? 20,
                    Sort = sort
                };
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .WithName("SearchCreditTopupRequests")
            .WithSummary("Search credit top-up requests")
            .RequirePermission(PpapPermissions.Credits.Manage)
            .Produces<PagedResponse<CreditTopupRequestDto>>(StatusCodes.Status200OK);
    }
}
