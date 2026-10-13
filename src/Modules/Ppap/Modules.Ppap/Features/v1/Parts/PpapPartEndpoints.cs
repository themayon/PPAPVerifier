using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Persistence;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Parts;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Ppap.Features.v1.Parts;

public static class SearchMyPpapPartsEndpoint
{
    internal static RouteHandlerBuilder MapSearchMyPpapPartsEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/parts", async (string? search, int? pageNumber, int? pageSize, string? sort, IMediator mediator, CancellationToken ct) =>
        {
            SearchMyPpapPartsQuery query = new() { Search = search, PageNumber = pageNumber ?? 1, PageSize = pageSize ?? 20, Sort = sort };
            return Results.Ok(await mediator.Send(query, ct));
        }).WithName("SearchMyPpapParts").WithSummary("Search my PPAP parts")
            .RequirePermission(PpapPermissions.Parts.View).Produces<PagedResponse<PpapPartDto>>();
}

public static class CreatePpapPartEndpoint
{
    internal static RouteHandlerBuilder MapCreatePpapPartEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/parts", async ([FromBody] CreatePartCommand command, IMediator mediator, CancellationToken ct) =>
        {
            PpapPartDto part = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/ppap/parts/{part.Id}", part);
        }).WithName("CreatePpapPart").WithSummary("Create a PPAP part")
            .RequirePermission(PpapPermissions.Parts.Create).WithIdempotency().Produces<PpapPartDto>(StatusCodes.Status201Created);
}

public static class GetPpapPartEndpoint
{
    internal static RouteHandlerBuilder MapGetPpapPartEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/parts/{id:guid}", async (Guid id, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new GetPartQuery(id), ct)))
            .WithName("GetPpapPart").WithSummary("Get my PPAP part")
            .RequirePermission(PpapPermissions.Parts.View).Produces<PpapPartDto>();
}

public static class UpdatePpapPartEndpoint
{
    internal static RouteHandlerBuilder MapUpdatePpapPartEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPut("/parts/{id:guid}", async (Guid id, [FromBody] UpdatePartRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new UpdatePartCommand(id, body.PartName, body.PartNumber, body.DrawingRevision, body.ExpectedVersion), ct)))
            .WithName("UpdatePpapPart").WithSummary("Update my PPAP part")
            .RequirePermission(PpapPermissions.Parts.Update).Produces<PpapPartDto>();

    public sealed record UpdatePartRequest(string PartName, string PartNumber, string DrawingRevision, int ExpectedVersion);
}

public static class AssignPpapPartTemplateEndpoint
{
    internal static RouteHandlerBuilder MapAssignPpapPartTemplateEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/parts/{id:guid}/assign-template", async (Guid id, [FromBody] AssignTemplateRequest body, IMediator mediator, CancellationToken ct) =>
            Results.Ok(await mediator.Send(new AssignPartTemplateCommand(id, body.RuleTemplateId, body.ExpectedVersion), ct)))
            .WithName("AssignPpapPartTemplate").WithSummary("Assign the current published template to my PPAP part")
            .RequirePermission(PpapPermissions.Parts.AssignTemplate).WithIdempotency().Produces<PpapPartDto>();

    public sealed record AssignTemplateRequest(Guid RuleTemplateId, int ExpectedVersion);
}

public static class CreateDraftFromPpapPartEndpoint
{
    internal static RouteHandlerBuilder MapCreateDraftFromPpapPartEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/parts/{id:guid}/submissions", async (Guid id, [FromBody] CreatePartSubmissionRequest body, IMediator mediator, CancellationToken ct) =>
        {
            Guid submissionId = await mediator.Send(new CreateDraftFromPartCommand(id, body.PpapLevel), ct);
            return Results.Created($"/api/v1/ppap/submissions/{submissionId}", submissionId);
        }).WithName("CreateDraftFromPpapPart").WithSummary("Create a draft submission from my PPAP part")
            .RequirePermission(PpapPermissions.Submissions.Create).WithIdempotency().Produces<Guid>(StatusCodes.Status201Created);

    public sealed record CreatePartSubmissionRequest(int PpapLevel);
}
