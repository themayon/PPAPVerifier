using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Reports;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Reports.WaivePpapFinding;

public static class CreatePpapFindingWaiverEndpoint
{
    internal static RouteHandlerBuilder MapCreatePpapFindingWaiverEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/operations/reports/finding-waivers", async (WaivePpapFindingCommand command, IMediator mediator, CancellationToken cancellationToken) =>
                Results.Created($"/api/v1/ppap/operations/reports/{command.ReportId}/review", await mediator.Send(command, cancellationToken)))
            .WithName("WaivePpapFinding")
            .WithSummary("Waive an automated or manual PPAP finding")
            .RequirePermission(PpapPermissions.Findings.ManageWaivers)
            .WithIdempotency()
            .Produces<Guid>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
}
