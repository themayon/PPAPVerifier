using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Modules.Catalog.Contracts.v1.Categories;
using DES.Modules.Catalog.Data;
using DES.Modules.Catalog.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Catalog.Features.v1.Categories.CreateCategory;

public sealed class CreateCategoryCommandHandler(CatalogDbContext dbContext)
    : ICommandHandler<CreateCategoryCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.ParentCategoryId is { } parentId)
        {
            bool parentExists = await dbContext.Categories
                .AnyAsync(c => c.Id == parentId, cancellationToken)
                .ConfigureAwait(false);
            if (!parentExists)
            {
                throw new NotFoundException($"Parent category {parentId} not found.");
            }
        }

        var category = Category.Create(command.Name, command.Description, command.ParentCategoryId);

        bool slugTaken = await dbContext.Categories
            .AnyAsync(c => c.Slug == category.Slug, cancellationToken)
            .ConfigureAwait(false);
        if (slugTaken)
        {
            throw new CustomException(
                $"A category with name '{command.Name}' already exists.",
                (IEnumerable<string>?)null,
                HttpStatusCode.Conflict);
        }

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return category.Id;
    }
}
