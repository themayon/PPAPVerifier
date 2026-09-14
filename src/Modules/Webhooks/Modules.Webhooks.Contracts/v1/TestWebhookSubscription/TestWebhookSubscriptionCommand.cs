using Mediator;

namespace DES.Modules.Webhooks.Contracts.v1.TestWebhookSubscription;

public sealed record TestWebhookSubscriptionCommand(Guid Id) : ICommand<bool>;
