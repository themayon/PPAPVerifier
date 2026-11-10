using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.DecidePpapSubmission;

public static class UpdatePpapSubmissionDecisionEndpoint
{
    internal static RouteHandlerBuilder MapDecidePpapSubmissionEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/submissions/{id:guid}/decision", async (Guid id, [FromBody] DecidePpapSubmissionRequest body, IMediator mediator, CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new DecidePpapSubmissionCommand(id, body.Decision, body.Comment, body.ExpectedVersion), cancellationToken)))
            .WithName("DecidePpapSubmission")
            .WithSummary("Approve, interim-approve, or reject a submitted PPAP")
            .RequirePermission(PpapPermissions.Submissions.Approve)
            .WithIdempotency()
            .Produces<PpapSubmissionDecisionDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);

    public sealed record DecidePpapSubmissionRequest(string Decision, string? Comment, int ExpectedVersion);
}
