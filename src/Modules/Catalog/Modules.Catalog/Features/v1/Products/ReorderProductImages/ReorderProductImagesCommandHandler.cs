using DES.Framework.Core.Exceptions;
using DES.Modules.Catalog.Contracts.v1.Products.ReorderProductImages;
using DES.Modules.Catalog.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Catalog.Features.v1.Products.ReorderProductImages;

public sealed class ReorderProductImagesCommandHandler(CatalogDbContext dbContext)
    : ICommandHandler<ReorderProductImagesCommand, Unit>
{
    public async ValueTask<Unit> Handle(ReorderProductImagesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var product = await dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == command.ProductId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Product {command.ProductId} not found.");

        product.ReorderImages(command.OrderedImageIds);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Unit.Value;
    }
}
