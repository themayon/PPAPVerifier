using DES.Modules.Identity.Contracts.DTOs;
using DES.Modules.Identity.Contracts.Services;
using DES.Modules.Identity.Contracts.v1.Users.GetUsers;
using Mediator;

namespace DES.Modules.Identity.Features.v1.Users.GetUsers;

public sealed class GetUsersQueryHandler : IQueryHandler<GetUsersQuery, List<UserDto>>
{
    private readonly IUserService _userService;

    public GetUsersQueryHandler(IUserService userService)
    {
        _userService = userService;
    }

    public async ValueTask<List<UserDto>> Handle(GetUsersQuery query, CancellationToken cancellationToken)
    {
        return await _userService.GetListAsync(cancellationToken).ConfigureAwait(false);
    }
}