using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Reports;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Reports.GetPpapReport;

public static class GetPpapReportEndpoint
{
    internal static RouteHandlerBuilder MapGetPpapReportEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/reports/{id:guid}", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetPpapReportQuery(id), cancellationToken)))
            .WithName("GetPpapReport")
            .WithSummary("Get a PPAP report")
            .RequirePermission(PpapPermissions.Submissions.View)
            .Produces<PpapReportDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
