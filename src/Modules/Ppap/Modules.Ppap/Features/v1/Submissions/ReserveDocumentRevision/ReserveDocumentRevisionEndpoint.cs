using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.ReserveDocumentRevision;

public static class CreateDocumentRevisionEndpoint
{
    internal static RouteHandlerBuilder MapCreateDocumentRevisionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/submissions/{submissionId:guid}/documents/{documentId:guid}/revisions", async (
                Guid submissionId,
                Guid documentId,
                [FromBody] ReserveDocumentRevisionRequest body,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new ReserveDocumentRevisionCommand(
                    submissionId,
                    documentId,
                    body.ExpectedVersion,
                    body.FileName,
                    body.ContentType,
                    body.SizeBytes), cancellationToken)))
            .WithName("ReserveDocumentRevision")
            .WithSummary("Reserve a new primary file revision for a draft PPAP document")
            .RequirePermission(PpapPermissions.Submissions.Update)
            .WithIdempotency()
            .Produces<Guid>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    public sealed record ReserveDocumentRevisionRequest(
        int ExpectedVersion,
        string FileName,
        string ContentType,
        long SizeBytes);
}
