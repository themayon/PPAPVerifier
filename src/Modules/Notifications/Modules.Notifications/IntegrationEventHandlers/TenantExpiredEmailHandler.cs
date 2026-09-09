using DES.Framework.Eventing.Abstractions;
using DES.Framework.Mailing.Services;
using DES.Modules.Multitenancy.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace DES.Modules.Notifications.IntegrationEventHandlers;

/// <summary>Emails the tenant admin that their subscription expired and access is suspended.</summary>
public sealed class TenantExpiredEmailHandler(
    IMailService mailService,
    ILogger<TenantExpiredEmailHandler> logger)
    : IIntegrationEventHandler<TenantExpiredIntegrationEvent>
{
    public async Task HandleAsync(TenantExpiredIntegrationEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var (subject, body) = BillingEmailBodies.Expired(@event.TenantName, @event.PlanKey, @event.ValidUpto);
        await BillingEmailSender.SendAsync(mailService, logger, @event.AdminEmail, subject, body, "expired", ct)
            .ConfigureAwait(false);
    }
}
