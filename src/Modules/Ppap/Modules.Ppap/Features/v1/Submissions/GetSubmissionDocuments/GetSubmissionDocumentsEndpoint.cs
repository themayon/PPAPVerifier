using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.GetSubmissionDocuments;

public static class GetSubmissionDocumentsEndpoint
{
    internal static RouteHandlerBuilder MapGetSubmissionDocumentsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/submissions/{id:guid}/documents", async (
                Guid id,
                IMediator mediator,
                CancellationToken cancellationToken) =>
                Results.Ok(await mediator.Send(new ListSubmissionDocumentsQuery(id), cancellationToken)))
            .WithName("GetSubmissionDocuments")
            .WithSummary("Get document slots for a PPAP submission")
            .RequirePermission(PpapPermissions.Submissions.View)
            .Produces<IReadOnlyList<SubmissionDocumentDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);
    }
}
