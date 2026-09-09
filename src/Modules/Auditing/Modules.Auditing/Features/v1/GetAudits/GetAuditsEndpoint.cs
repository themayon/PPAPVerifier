using DES.Modules.Auditing.Contracts.Authorization;
using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Persistence;
using DES.Modules.Auditing.Contracts.Dtos;
using DES.Modules.Auditing.Contracts.v1.GetAudits;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Auditing.Features.v1.GetAudits;

public static class GetAuditsEndpoint
{
    public static RouteHandlerBuilder MapGetAuditsEndpoint(this IEndpointRouteBuilder group)
    {
        return group.MapGet(
                "/",
                async ([AsParameters] GetAuditsQuery query, IMediator mediator, CancellationToken cancellationToken) =>
                    TypedResults.Ok(await mediator.Send(query, cancellationToken)))
            .WithName("GetAudits")
            .WithSummary("List and search audit events")
            .WithDescription("Retrieve audit events with pagination and filters.")
            .RequirePermission(AuditingPermissions.AuditTrails.View)
            .Produces<PagedResponse<AuditSummaryDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);
    }
}