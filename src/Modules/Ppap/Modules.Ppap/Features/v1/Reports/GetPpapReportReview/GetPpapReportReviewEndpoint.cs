using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Reports;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Reports.GetPpapReportReview;

public static class GetPpapReportReviewEndpoint
{
    internal static RouteHandlerBuilder MapGetPpapReportReviewEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/operations/reports/{id:guid}/review", async (Guid id, IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetPpapReportReviewQuery(id), cancellationToken)))
            .WithName("GetPpapReportReview")
            .WithSummary("Get a PPAP report finding review")
            .RequirePermission(PpapPermissions.Findings.View)
            .Produces<PpapReportDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
}
