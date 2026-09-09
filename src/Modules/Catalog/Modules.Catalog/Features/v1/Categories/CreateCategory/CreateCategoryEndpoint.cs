using DES.Modules.Catalog.Contracts.Authorization;
using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Catalog.Contracts.v1.Categories;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Catalog.Features.v1.Categories.CreateCategory;

public static class CreateCategoryEndpoint
{
    internal static RouteHandlerBuilder MapCreateCategoryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/categories",
                async (CreateCategoryCommand command, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(command, ct)))
            .WithName("CreateCategory")
            .WithSummary("Create a category")
            .RequirePermission(CatalogPermissions.Categories.Create)
            .WithIdempotency();
    }
}
