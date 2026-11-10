using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Workspace.DecideFindingWaiverRequest;

public static class UpdateFindingWaiverRequestEndpoint
{
    internal static RouteHandlerBuilder MapUpdateFindingWaiverRequestEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/operations/waiver-requests/{requestId:guid}/decision", async (
                Guid requestId,
                [FromBody] DecideFindingWaiverRequest body,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new DecideFindingWaiverRequestCommand(
                    requestId,
                    body.Approve,
                    body.Reason,
                    body.ExpectedVersion), cancellationToken);
                return Results.NoContent();
            })
            .WithName("DecideFindingWaiverRequest")
            .WithSummary("Approve or reject a PPAP finding waiver request")
            .RequirePermission(PpapPermissions.Findings.ManageWaivers)
            .WithIdempotency()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    public sealed record DecideFindingWaiverRequest(bool Approve, string Reason, long ExpectedVersion);
}
