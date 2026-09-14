using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Webhooks.Contracts.Authorization;
using DES.Modules.Webhooks.Contracts.v1.CreateWebhookSubscription;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Webhooks.Features.v1.CreateWebhookSubscription;

public static class CreateWebhookSubscriptionEndpoint
{
    internal static RouteHandlerBuilder MapCreateWebhookSubscriptionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/subscriptions", async (
            CreateWebhookSubscriptionCommand command,
            IMediator mediator,
            CancellationToken ct) =>
        {
            var id = await mediator.Send(command, ct);
            return TypedResults.Created($"/api/v1/webhooks/subscriptions/{id}", id);
        })
        .WithName("CreateWebhookSubscription")
        .WithSummary("Create a webhook subscription")
        .RequirePermission(WebhooksPermissions.Subscriptions.Create)
        .WithIdempotency()
        .Produces<Guid>(StatusCodes.Status201Created);
    }
}
