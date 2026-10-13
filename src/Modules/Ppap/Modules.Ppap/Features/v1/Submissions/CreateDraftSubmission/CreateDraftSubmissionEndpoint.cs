using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Submissions.CreateDraftSubmission;

public static class CreateDraftSubmissionEndpoint
{
    internal static RouteHandlerBuilder MapCreateDraftSubmissionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/submissions", async (
                CreateDraftSubmissionCommand command,
                IMediator mediator,
                CancellationToken ct) =>
            {
                Guid id = await mediator.Send(command, ct);
                return Results.Created($"/api/v1/ppap/submissions/{id}", id);
            })
            .WithName("CreateDraftSubmission")
            .WithSummary("Create a draft PPAP submission")
            .RequirePermission(PpapPermissions.Submissions.Create)
            .WithIdempotency()
            .Produces<Guid>(StatusCodes.Status201Created);
    }
}
