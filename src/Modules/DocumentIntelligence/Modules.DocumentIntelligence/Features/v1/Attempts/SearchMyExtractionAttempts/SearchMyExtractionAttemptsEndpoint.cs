using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Authorization;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.DocumentIntelligence.Features.v1.Attempts.SearchMyExtractionAttempts;

public static class SearchMyExtractionAttemptsEndpoint
{
    internal static RouteHandlerBuilder MapSearchMyExtractionAttemptsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/attempts", async (
                string? status,
                int? pageNumber,
                int? pageSize,
                string? sort,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                SearchMyExtractionAttemptsQuery query = new()
                {
                    Status = status,
                    PageNumber = pageNumber ?? 1,
                    PageSize = pageSize ?? 20,
                    Sort = sort
                };
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .WithName("SearchMyExtractionAttempts")
            .WithSummary("Search my document extraction attempts")
            .RequirePermission(ExtractionPermissions.Attempts.View)
            .Produces<PagedResponse<ExtractionAttemptDto>>(StatusCodes.Status200OK);
    }
}
