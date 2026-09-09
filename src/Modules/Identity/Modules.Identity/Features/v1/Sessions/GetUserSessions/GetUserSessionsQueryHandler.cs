using DES.Modules.Identity.Contracts.DTOs;
using DES.Modules.Identity.Contracts.Services;
using DES.Modules.Identity.Contracts.v1.Sessions.GetUserSessions;
using Mediator;

namespace DES.Modules.Identity.Features.v1.Sessions.GetUserSessions;

public sealed class GetUserSessionsQueryHandler : IQueryHandler<GetUserSessionsQuery, List<UserSessionDto>>
{
    private readonly ISessionService _sessionService;

    public GetUserSessionsQueryHandler(ISessionService sessionService)
    {
        _sessionService = sessionService;
    }

    public async ValueTask<List<UserSessionDto>> Handle(GetUserSessionsQuery query, CancellationToken cancellationToken)
    {
        return await _sessionService.GetUserSessionsForAdminAsync(query.UserId.ToString(), cancellationToken);
    }
}