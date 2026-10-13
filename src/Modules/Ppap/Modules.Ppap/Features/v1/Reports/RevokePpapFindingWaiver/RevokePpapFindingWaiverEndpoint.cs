using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Reports;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Reports.RevokePpapFindingWaiver;

public static class RevokePpapFindingWaiverEndpoint
{
    internal static RouteHandlerBuilder MapRevokePpapFindingWaiverEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/operations/reports/finding-waivers/revoke", async (RevokePpapFindingWaiverCommand command, IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(command, cancellationToken)))
            .WithName("RevokePpapFindingWaiver")
            .WithSummary("Revoke an active PPAP finding waiver")
            .RequirePermission(PpapPermissions.Findings.ManageWaivers)
            .WithIdempotency()
            .Produces<Guid>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
}
