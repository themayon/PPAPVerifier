using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Workspace;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Workspace.GetSubmissionWorkspace;

public static class GetSubmissionWorkspaceEndpoint
{
    internal static RouteHandlerBuilder MapGetSubmissionWorkspaceEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/submissions/{submissionId:guid}/workspace", async (
                Guid submissionId,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new GetSubmissionWorkspaceQuery(submissionId),
                    cancellationToken)))
            .WithName("GetSubmissionWorkspace")
            .WithSummary("Get the document workspace for an owned PPAP submission")
            .RequirePermission(PpapPermissions.Submissions.View)
            .Produces<SubmissionWorkspaceDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
