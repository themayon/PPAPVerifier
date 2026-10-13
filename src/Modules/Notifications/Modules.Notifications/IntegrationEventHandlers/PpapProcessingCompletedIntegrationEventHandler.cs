using DES.Framework.Eventing.Abstractions;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Web.Realtime;
using DES.Modules.Notifications.Data;
using DES.Modules.Notifications.Domain;
using DES.Modules.Ppap.Contracts.Events;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace DES.Modules.Notifications.IntegrationEventHandlers;

public sealed class PpapProcessingCompletedIntegrationEventHandler(
    NotificationsDbContext dbContext,
    IHubContext<AppHub> hub,
    IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor)
    : IIntegrationEventHandler<PpapProcessingCompletedIntegrationEvent>
{
    public async Task HandleAsync(PpapProcessingCompletedIntegrationEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        string? ambientTenantId = tenantAccessor.MultiTenantContext.TenantInfo?.Id;
        if (!string.Equals(ambientTenantId, @event.TenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Tenant context mismatch handling {nameof(PpapProcessingCompletedIntegrationEvent)}.");
        }

        Notification notification = Notification.Create(
            @event.OwnerUserId.ToString(),
            "ppap.processing.completed",
            "PPAP report ready",
            $"The report for {@event.PartName} ({@event.PartNumber}) is ready.",
            $"/ppap/reports/{@event.ReportId}",
            @event.Source,
            new { @event.JobId, @event.SubmissionId, @event.ReportId });
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        await hub.Clients.Group($"user:{@event.OwnerUserId}")
            .SendAsync("NotificationCreated", new
            {
                id = notification.Id,
                type = notification.Type,
                title = notification.Title,
                body = notification.Body,
                link = notification.Link,
                source = notification.Source,
                createdAtUtc = notification.CreatedAtUtc
            }, ct)
            .ConfigureAwait(false);
    }
}
