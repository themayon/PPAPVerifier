using System.Diagnostics.CodeAnalysis;
using DES.Framework.Eventing.Abstractions;
using DES.Framework.Jobs.Services;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Processing;

namespace DES.Modules.Ppap.IntegrationEventHandlers;

[SuppressMessage("Naming", "CA1711", Justification = "The required integration-event handler name describes its role.")]
public sealed class PpapSubmissionQueuedIntegrationEventHandler(IJobService jobService)
    : IIntegrationEventHandler<PpapSubmissionQueuedIntegrationEvent>
{
    public Task HandleAsync(
        PpapSubmissionQueuedIntegrationEvent @event,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ct.ThrowIfCancellationRequested();
        jobService.Enqueue<PpapProcessingJob>(
            job => job.RunAsync(@event.TenantId, @event.JobId, CancellationToken.None));
        return Task.CompletedTask;
    }
}
