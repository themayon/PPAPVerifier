using DES.Modules.Identity.Contracts.DTOs;
using Mediator;

namespace DES.Modules.Identity.Contracts.v1.Roles.GetRoleWithPermissions;

public sealed record GetRoleWithPermissionsQuery(string Id) : IQuery<RoleDto>;