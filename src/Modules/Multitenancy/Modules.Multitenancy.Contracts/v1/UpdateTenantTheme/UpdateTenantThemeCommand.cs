using DES.Modules.Multitenancy.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Multitenancy.Contracts.v1.UpdateTenantTheme;

public sealed record UpdateTenantThemeCommand(TenantThemeDto Theme) : ICommand;