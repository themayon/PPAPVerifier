using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Workspace.RequestFindingWaiver;

public static class RequestFindingWaiverEndpoint
{
    internal static RouteHandlerBuilder MapRequestFindingWaiverEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/submissions/{submissionId:guid}/findings/{findingId:guid}/waiver-requests", async (
                Guid submissionId,
                Guid findingId,
                [FromBody] RequestFindingWaiverRequest body,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new RequestFindingWaiverCommand(submissionId, findingId, body.Reason),
                    cancellationToken)))
            .WithName("RequestFindingWaiver")
            .WithSummary("Request a waiver for a current PPAP working finding")
            .RequirePermission(PpapPermissions.Findings.RequestWaiver)
            .WithIdempotency()
            .Produces<Guid>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    public sealed record RequestFindingWaiverRequest(string Reason);
}
