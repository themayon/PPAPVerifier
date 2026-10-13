using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.DocumentIntelligence.Contracts.Authorization;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.v1.Operations;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.DocumentIntelligence.Features.v1.Operations.GetOperatorExtractionAttempt;

public static class GetOperatorExtractionAttemptEndpoint
{
    internal static RouteHandlerBuilder MapGetOperatorExtractionAttemptEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/operations/attempts/{id:guid}", async (
                Guid id,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetOperatorExtractionAttemptQuery(id), cancellationToken)))
            .WithName("GetOperatorExtractionAttempt")
            .WithSummary("Get a tenant document extraction attempt for operations")
            .RequirePermission(ExtractionPermissions.Attempts.View)
            .Produces<OperatorExtractionAttemptDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
