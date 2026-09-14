using DES.Modules.Catalog.Contracts.Authorization;
using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Catalog.Contracts.v1.Brands;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Catalog.Features.v1.Brands.CreateBrand;

public static class CreateBrandEndpoint
{
    internal static RouteHandlerBuilder MapCreateBrandEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/brands",
                async (CreateBrandCommand command, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(command, ct)))
            .WithName("CreateBrand")
            .WithSummary("Create a brand")
            .RequirePermission(CatalogPermissions.Brands.Create)
            .WithIdempotency();
    }
}
