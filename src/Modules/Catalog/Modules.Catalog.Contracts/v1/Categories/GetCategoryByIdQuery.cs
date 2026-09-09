using DES.Modules.Catalog.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Catalog.Contracts.v1.Categories;

public sealed record GetCategoryByIdQuery(Guid CategoryId) : IQuery<CategoryDto>;
