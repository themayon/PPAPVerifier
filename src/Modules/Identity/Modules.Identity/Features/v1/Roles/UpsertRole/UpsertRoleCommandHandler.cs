using DES.Modules.Identity.Contracts.DTOs;
using DES.Modules.Identity.Contracts.Services;
using DES.Modules.Identity.Contracts.v1.Roles.UpsertRole;
using Mediator;

namespace DES.Modules.Identity.Features.v1.Roles.UpsertRole;

public sealed class UpsertRoleCommandHandler : ICommandHandler<UpsertRoleCommand, RoleDto>
{
    private readonly IRoleService _roleService;

    public UpsertRoleCommandHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public async ValueTask<RoleDto> Handle(UpsertRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await _roleService.CreateOrUpdateRoleAsync(command.Id, command.Name, command.Description ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);
    }
}