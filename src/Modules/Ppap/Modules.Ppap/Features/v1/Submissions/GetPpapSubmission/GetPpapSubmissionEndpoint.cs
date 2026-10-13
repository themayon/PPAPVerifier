using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.GetPpapSubmission;

public static class GetPpapSubmissionEndpoint
{
    internal static RouteHandlerBuilder MapGetPpapSubmissionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/submissions/{id:guid}", async (
                Guid id,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetPpapSubmissionQuery(id), cancellationToken)))
            .WithName("GetPpapSubmission")
            .WithSummary("Get a PPAP submission")
            .RequirePermission(PpapPermissions.Submissions.View)
            .Produces<PpapSubmissionDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
