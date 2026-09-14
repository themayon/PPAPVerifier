using DES.Modules.Catalog.Contracts.Authorization;
using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Catalog.Contracts.v1.Categories;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Catalog.Features.v1.Categories.UpdateCategory;

public static class UpdateCategoryEndpoint
{
    internal static RouteHandlerBuilder MapUpdateCategoryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPut("/categories/{categoryId:guid}",
                async (Guid categoryId, UpdateCategoryCommand body, IMediator mediator, CancellationToken ct) =>
                {
                    ArgumentNullException.ThrowIfNull(body);
                    var command = body with { CategoryId = categoryId };
                    return Results.Ok(await mediator.Send(command, ct));
                })
            .WithName("UpdateCategory")
            .WithSummary("Update a category")
            .RequirePermission(CatalogPermissions.Categories.Update);
    }
}
