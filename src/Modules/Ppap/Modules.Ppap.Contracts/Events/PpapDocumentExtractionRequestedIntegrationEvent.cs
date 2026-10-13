using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.Ppap.Contracts.Events;

public sealed record PpapDocumentExtractionRequestedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string TenantId,
    string CorrelationId,
    string Source,
    Guid JobId,
    Guid SubmissionId,
    Guid SubmissionDocumentId,
    Guid OwnerUserId,
    Guid FileAssetId,
    string DocumentCode) : IIntegrationEvent;
