using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Authorization;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.v1.Operations;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.DocumentIntelligence.Features.v1.Operations.SearchOperatorExtractionAttempts;

public static class SearchOperatorExtractionAttemptsEndpoint
{
    internal static RouteHandlerBuilder MapSearchOperatorExtractionAttemptsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/operations/attempts", async (
                string? status,
                string? documentCode,
                Guid? ownerUserId,
                int? pageNumber,
                int? pageSize,
                string? sort,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                SearchOperatorExtractionAttemptsQuery query = new()
                {
                    Status = status,
                    DocumentCode = documentCode,
                    OwnerUserId = ownerUserId,
                    PageNumber = pageNumber ?? 1,
                    PageSize = pageSize ?? 20,
                    Sort = sort
                };
                return Results.Ok(await mediator.Send(query, cancellationToken));
            })
            .WithName("SearchOperatorExtractionAttempts")
            .WithSummary("Search tenant document extraction attempts for operations")
            .RequirePermission(ExtractionPermissions.Attempts.View)
            .Produces<PagedResponse<OperatorExtractionAttemptDto>>(StatusCodes.Status200OK);
    }
}
