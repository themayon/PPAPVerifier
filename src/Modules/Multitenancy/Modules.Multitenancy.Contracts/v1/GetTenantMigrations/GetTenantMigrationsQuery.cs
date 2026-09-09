using DES.Modules.Multitenancy.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Multitenancy.Contracts.v1.GetTenantMigrations;

public sealed record GetTenantMigrationsQuery : IQuery<IReadOnlyCollection<TenantMigrationStatusDto>>;