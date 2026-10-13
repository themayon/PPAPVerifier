using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.DocumentIntelligence.Contracts.Authorization;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.DocumentIntelligence.Features.v1.Attempts.GetExtractionAttempt;

public static class GetExtractionAttemptEndpoint
{
    internal static RouteHandlerBuilder MapGetExtractionAttemptEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/attempts/{id:guid}", async (
                Guid id,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetExtractionAttemptQuery(id), cancellationToken)))
            .WithName("GetExtractionAttempt")
            .WithSummary("Get a document extraction attempt")
            .RequirePermission(ExtractionPermissions.Attempts.View)
            .Produces<ExtractionAttemptDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
