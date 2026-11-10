using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Workspace.GetPendingWaiverRequests;

public static class GetPendingWaiverRequestsEndpoint
{
    internal static RouteHandlerBuilder MapGetPendingWaiverRequestsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/operations/submissions/{submissionId:guid}/waiver-requests", async (
                Guid submissionId,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new GetPendingWaiverRequestsQuery(submissionId), cancellationToken)))
            .WithName("GetPendingWaiverRequests")
            .WithSummary("Get pending finding waiver requests for PPAP reviewer action")
            .RequirePermission(PpapPermissions.Findings.ManageWaivers)
            .Produces<IReadOnlyList<WaiverReviewDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
