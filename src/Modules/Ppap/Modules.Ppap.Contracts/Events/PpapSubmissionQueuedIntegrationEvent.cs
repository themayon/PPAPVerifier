using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.Ppap.Contracts.Events;

public sealed record PpapSubmissionQueuedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string TenantId,
    string CorrelationId,
    string Source,
    Guid SubmissionId,
    Guid JobId,
    int SubmissionVersion,
    Guid OwnerUserId) : IIntegrationEvent;
