using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Workspace.GetSpreadsheetViewerLicense;

public static class GetSpreadsheetViewerLicenseEndpoint
{
    internal static RouteHandlerBuilder MapGetSpreadsheetViewerLicenseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/viewer-license", async (
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetSpreadsheetViewerLicenseQuery(), cancellationToken)))
            .WithName("GetSpreadsheetViewerLicense")
            .WithSummary("Get the signed OmnexSpread browser license for an authenticated PPAP user")
            .RequirePermission(PpapPermissions.Submissions.View)
            .Produces<SpreadsheetViewerLicenseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status503ServiceUnavailable);
    }
}
