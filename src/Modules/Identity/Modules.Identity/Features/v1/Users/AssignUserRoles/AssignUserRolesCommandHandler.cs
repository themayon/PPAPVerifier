using DES.Modules.Identity.Contracts.Services;
using DES.Modules.Identity.Contracts.v1.Users.AssignUserRoles;
using Mediator;

namespace DES.Modules.Identity.Features.v1.Users.AssignUserRoles;

public sealed class AssignUserRolesCommandHandler(IUserService userService)
    : ICommandHandler<AssignUserRolesCommand, string>
{
    public async ValueTask<string> Handle(AssignUserRolesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return await userService.AssignRolesAsync(command.UserId, command.UserRoles, cancellationToken);
    }

}