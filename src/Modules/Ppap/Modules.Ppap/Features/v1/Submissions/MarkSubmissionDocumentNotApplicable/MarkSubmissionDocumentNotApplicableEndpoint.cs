using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.MarkSubmissionDocumentNotApplicable;

public static class MarkSubmissionDocumentNotApplicableEndpoint
{
    internal static RouteHandlerBuilder MapMarkSubmissionDocumentNotApplicableEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/submissions/{id:guid}/documents/{documentId:guid}/not-applicable", async (
                Guid id,
                Guid documentId,
                [FromBody] MarkSubmissionDocumentNotApplicableRequest body,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(
                    new MarkSubmissionDocumentNotApplicableCommand(
                        id,
                        documentId,
                        body.Reason),
                    cancellationToken);
                return Results.NoContent();
            })
            .WithName("MarkSubmissionDocumentNotApplicable")
            .WithSummary("Mark a PPAP submission document as not applicable")
            .RequirePermission(PpapPermissions.Submissions.Update)
            .WithIdempotency()
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    public sealed record MarkSubmissionDocumentNotApplicableRequest(string Reason);
}
