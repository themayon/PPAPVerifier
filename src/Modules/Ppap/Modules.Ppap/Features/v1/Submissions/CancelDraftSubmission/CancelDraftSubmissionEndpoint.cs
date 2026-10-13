using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.CancelDraftSubmission;

public static class UpdateDraftSubmissionStatusEndpoint
{
    internal static RouteHandlerBuilder MapCancelDraftSubmissionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/submissions/{id:guid}/cancel", async (
                Guid id,
                [FromBody] CancelDraftSubmissionRequest body,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new CancelDraftSubmissionCommand(id, body.ExpectedVersion),
                    cancellationToken)))
            .WithName("CancelDraftSubmission")
            .WithSummary("Cancel a draft PPAP submission")
            .RequirePermission(PpapPermissions.Submissions.Cancel)
            .WithIdempotency()
            .Produces<PpapSubmissionDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    public sealed record CancelDraftSubmissionRequest(int ExpectedVersion);
}
