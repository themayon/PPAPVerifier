using Finbuckle.MultiTenant.Abstractions;
using DES.Framework.Eventing.Abstractions;
using DES.Framework.Mailing.Services;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Billing.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace DES.Modules.Notifications.IntegrationEventHandlers;

/// <summary>Emails the tenant admin when an invoice is issued. Resolves the admin email from the tenant
/// store (the event only carries the tenant id).</summary>
public sealed class InvoiceIssuedEmailHandler(
    IMultiTenantStore<AppTenantInfo> tenantStore,
    IMailService mailService,
    ILogger<InvoiceIssuedEmailHandler> logger)
    : IIntegrationEventHandler<InvoiceIssuedIntegrationEvent>
{
    public async Task HandleAsync(InvoiceIssuedIntegrationEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (string.IsNullOrWhiteSpace(@event.TenantId))
        {
            return;
        }

        var tenant = await tenantStore.GetAsync(@event.TenantId).ConfigureAwait(false);
        if (tenant is null)
        {
            return;
        }

        var (subject, body) = BillingEmailBodies.InvoiceIssued(
            @event.InvoiceNumber, @event.Amount, @event.Currency, @event.DueAtUtc);
        await BillingEmailSender.SendAsync(mailService, logger, tenant.AdminEmail, subject, body, "invoice-issued", ct)
            .ConfigureAwait(false);
    }
}
