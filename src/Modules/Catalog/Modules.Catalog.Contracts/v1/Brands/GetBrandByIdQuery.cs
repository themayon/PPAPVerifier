using DES.Modules.Catalog.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Catalog.Contracts.v1.Brands;

public sealed record GetBrandByIdQuery(Guid BrandId) : IQuery<BrandDto>;
