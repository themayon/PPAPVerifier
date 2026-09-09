using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Catalog.Contracts.Authorization;
using DES.Modules.Catalog.Contracts.v1.Products;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Catalog.Features.v1.Products.ListTrashedProducts;

public static class ListTrashedProductsEndpoint
{
    internal static RouteHandlerBuilder MapListTrashedProductsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/products/trash",
                async (int? pageNumber, int? pageSize, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(
                        new ListTrashedProductsQuery(pageNumber ?? 1, pageSize ?? 20), ct)))
            .WithName("ListTrashedProducts")
            .WithSummary("List soft-deleted products")
            .RequirePermission(CatalogPermissions.Products.Restore);
    }
}
