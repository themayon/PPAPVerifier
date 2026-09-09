using DES.Framework.Core.Exceptions;
using DES.Framework.Persistence;
using DES.Modules.Catalog.Contracts.v1.Products;
using DES.Modules.Catalog.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Catalog.Features.v1.Products.RestoreProduct;

public sealed class RestoreProductCommandHandler(CatalogDbContext dbContext)
    : ICommandHandler<RestoreProductCommand, Guid>
{
    public async ValueTask<Guid> Handle(RestoreProductCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await dbContext.Products
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Product {command.ProductId} not found.");

        product.Restore();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return product.Id;
    }
}
