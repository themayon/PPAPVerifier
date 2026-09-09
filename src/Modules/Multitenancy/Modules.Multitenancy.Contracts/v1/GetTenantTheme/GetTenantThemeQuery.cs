using DES.Modules.Multitenancy.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Multitenancy.Contracts.v1.GetTenantTheme;

public sealed record GetTenantThemeQuery : IQuery<TenantThemeDto>;