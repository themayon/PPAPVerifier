using DES.Modules.Billing.Contracts.Authorization;
using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Billing.Contracts.v1.Subscriptions;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Billing.Features.v1.Subscriptions.AssignSubscription;

public static class AssignSubscriptionEndpoint
{
    internal static RouteHandlerBuilder MapAssignSubscriptionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/subscriptions",
                async (AssignSubscriptionCommand command, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(command, ct)))
            .WithName("AssignSubscription")
            .WithSummary("Assign a plan to a tenant")
            .RequirePermission(BillingPermissions.Manage)
            .WithIdempotency();
    }
}
