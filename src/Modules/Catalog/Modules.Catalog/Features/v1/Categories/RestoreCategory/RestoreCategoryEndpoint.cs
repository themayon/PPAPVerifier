using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Catalog.Contracts.Authorization;
using DES.Modules.Catalog.Contracts.v1.Categories;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Catalog.Features.v1.Categories.RestoreCategory;

public static class RestoreCategoryEndpoint
{
    internal static RouteHandlerBuilder MapRestoreCategoryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/categories/{categoryId:guid}/restore",
                async (Guid categoryId, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(new RestoreCategoryCommand(categoryId), ct)))
            .WithName("RestoreCategory")
            .WithSummary("Restore a soft-deleted category")
            .RequirePermission(CatalogPermissions.Categories.Restore)
            .WithIdempotency();
    }
}
