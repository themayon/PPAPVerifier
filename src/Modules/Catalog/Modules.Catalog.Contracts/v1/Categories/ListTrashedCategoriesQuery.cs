using DES.Framework.Shared.Persistence;
using DES.Modules.Catalog.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Catalog.Contracts.v1.Categories;

public sealed record ListTrashedCategoriesQuery(int PageNumber = 1, int PageSize = 20)
    : IQuery<PagedResponse<CategoryDto>>;
