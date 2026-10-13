using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.AddSubmissionDocument;

public static class AddSubmissionDocumentEndpoint
{
    internal static RouteHandlerBuilder MapAddSubmissionDocumentEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/submissions/{id:guid}/documents", async (
                Guid id,
                [FromBody] AddSubmissionDocumentRequest body,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(
                    new AddSubmissionDocumentCommand(id, body.DocumentCode),
                    cancellationToken)))
            .WithName("AddSubmissionDocument")
            .WithSummary("Add a document slot to a draft PPAP submission")
            .RequirePermission(PpapPermissions.Submissions.Update)
            .WithIdempotency()
            .Produces<Guid>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict);
    }

    public sealed record AddSubmissionDocumentRequest(string DocumentCode);
}
