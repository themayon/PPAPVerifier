using DES.Modules.Catalog.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Catalog.Contracts.v1.Products;

public sealed record GetProductByIdQuery(Guid ProductId) : IQuery<ProductDto>;
