using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.SubmitDraftSubmission;

public static class FinalizeDraftSubmissionEndpoint
{
    internal static RouteHandlerBuilder MapSubmitDraftSubmissionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/submissions/{id:guid}/submit", async (
                Guid id,
                [FromBody] SubmitDraftSubmissionRequest body,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new SubmitDraftSubmissionCommand(id, body.ExpectedVersion),
                    cancellationToken)))
            .WithName("SubmitDraftSubmission")
            .WithSummary("Submit a draft PPAP submission")
            .RequirePermission(PpapPermissions.Submissions.Submit)
            .WithIdempotency()
            .Produces<Guid>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    public sealed record SubmitDraftSubmissionRequest(int ExpectedVersion);
}
