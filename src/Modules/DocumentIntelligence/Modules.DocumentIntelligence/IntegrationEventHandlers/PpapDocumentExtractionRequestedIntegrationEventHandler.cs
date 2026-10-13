using System.Diagnostics.CodeAnalysis;
using DES.Framework.Eventing.Abstractions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;
using DES.Modules.Ppap.Contracts.Events;
using Finbuckle.MultiTenant.Abstractions;

namespace DES.Modules.DocumentIntelligence.IntegrationEventHandlers;

[SuppressMessage("Naming", "CA1711", Justification = "The required integration-event handler name describes its role.")]
public sealed class PpapDocumentExtractionRequestedIntegrationEventHandler(
    IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor,
    IDocumentExtractionRequestService requestService)
    : IIntegrationEventHandler<PpapDocumentExtractionRequestedIntegrationEvent>
{
    public async Task HandleAsync(
        PpapDocumentExtractionRequestedIntegrationEvent @event,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        string? ambientTenantId = tenantAccessor.MultiTenantContext.TenantInfo?.Id;
        if (!string.Equals(ambientTenantId, @event.TenantId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Tenant context mismatch handling {nameof(PpapDocumentExtractionRequestedIntegrationEvent)}.");
        }

        await requestService.RequestAsync(
            @event.OwnerUserId,
            @event.SubmissionDocumentId,
            @event.FileAssetId,
            @event.DocumentCode,
            ct).ConfigureAwait(false);
    }
}
