using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.DeleteDocumentRevision;

public static class DeleteDocumentRevisionEndpoint
{
    internal static RouteHandlerBuilder MapDeleteDocumentRevisionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapDelete("/submissions/{submissionId:guid}/documents/{documentId:guid}/revisions/{revisionId:guid}", async (
                Guid submissionId,
                Guid documentId,
                Guid revisionId,
                int expectedVersion,
                IMediator mediator,
                CancellationToken cancellationToken) =>
            {
                await mediator.Send(new DeleteDocumentRevisionCommand(submissionId, documentId, revisionId, expectedVersion), cancellationToken).ConfigureAwait(false);
                return Results.NoContent();
            })
            .WithName("DeleteDocumentRevision")
            .WithSummary("Delete the current file revision of a draft PPAP document so a new file can be uploaded")
            .RequirePermission(PpapPermissions.Submissions.Update)
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }
}
