using DES.Modules.Identity.Contracts.DTOs;
using DES.Modules.Identity.Contracts.Services;
using DES.Modules.Identity.Contracts.v1.Roles.GetRole;
using Mediator;

namespace DES.Modules.Identity.Features.v1.Roles.GetRoleById;

public sealed class GetRoleByIdQueryHandler : IQueryHandler<GetRoleQuery, RoleDto?>
{
    private readonly IRoleService _roleService;

    public GetRoleByIdQueryHandler(IRoleService roleService)
    {
        _roleService = roleService;
    }

    public async ValueTask<RoleDto?> Handle(GetRoleQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await _roleService.GetRoleAsync(query.Id, cancellationToken).ConfigureAwait(false);
    }
}