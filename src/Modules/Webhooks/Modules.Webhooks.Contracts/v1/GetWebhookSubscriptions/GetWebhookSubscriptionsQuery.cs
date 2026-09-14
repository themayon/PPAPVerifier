using DES.Framework.Shared.Persistence;
using DES.Modules.Webhooks.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Webhooks.Contracts.v1.GetWebhookSubscriptions;

public sealed record GetWebhookSubscriptionsQuery(int PageNumber = 1, int PageSize = 10)
    : IQuery<PagedResponse<WebhookSubscriptionDto>>;
