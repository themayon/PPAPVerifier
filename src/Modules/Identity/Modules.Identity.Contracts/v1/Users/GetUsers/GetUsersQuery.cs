using DES.Modules.Identity.Contracts.DTOs;
using Mediator;

namespace DES.Modules.Identity.Contracts.v1.Users.GetUsers;

public sealed record GetUsersQuery : IQuery<List<UserDto>>;