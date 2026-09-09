using DES.Framework.Core.Exceptions;
using DES.Modules.Catalog.Contracts.Dtos;
using DES.Modules.Catalog.Contracts.v1.Brands;
using DES.Modules.Catalog.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Catalog.Features.v1.Brands.GetBrandById;

public sealed class GetBrandByIdQueryHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetBrandByIdQuery, BrandDto>
{
    public async ValueTask<BrandDto> Handle(GetBrandByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var brand = await dbContext.Brands
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == query.BrandId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Brand {query.BrandId} not found.");

        return new BrandDto(
            brand.Id,
            brand.Name,
            brand.Slug,
            brand.Description,
            brand.LogoUrl,
            brand.CreatedAtUtc,
            brand.UpdatedAtUtc,
            brand.DeletedOnUtc,
            brand.DeletedBy);
    }
}
