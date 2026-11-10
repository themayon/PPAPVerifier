using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.Ppap.Contracts.Events;

public sealed record PpapDocumentRevisionExtractionRequestedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string TenantId,
    string CorrelationId,
    string Source,
    Guid SubmissionId,
    Guid SubmissionDocumentId,
    Guid DocumentRevisionId,
    Guid OwnerUserId,
    Guid FileAssetId,
    string DocumentCode) : IIntegrationEvent;
