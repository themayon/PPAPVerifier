using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.UpdateDraftSubmission;

public static class UpdateDraftSubmissionEndpoint
{
    internal static RouteHandlerBuilder MapUpdateDraftSubmissionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPut("/submissions/{id:guid}", async (
                Guid id,
                [FromBody] UpdateDraftSubmissionRequest body,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new UpdateDraftSubmissionCommand(
                    id,
                    body.ExpectedVersion,
                    body.PartName,
                    body.PartNumber,
                    body.DrawingRevision,
                    body.PpapLevel), cancellationToken)))
            .WithName("UpdateDraftSubmission")
            .WithSummary("Update a draft PPAP submission")
            .RequirePermission(PpapPermissions.Submissions.Update)
            .Produces<PpapSubmissionDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    public sealed record UpdateDraftSubmissionRequest(
        int ExpectedVersion,
        string PartName,
        string PartNumber,
        string DrawingRevision,
        int PpapLevel);
}
