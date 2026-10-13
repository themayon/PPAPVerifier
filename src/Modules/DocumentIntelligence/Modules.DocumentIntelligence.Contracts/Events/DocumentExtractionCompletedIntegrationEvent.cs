using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.DocumentIntelligence.Contracts.Events;

public sealed record DocumentExtractionCompletedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string TenantId,
    string CorrelationId,
    string Source,
    Guid ExtractionAttemptId,
    Guid SubmissionDocumentId,
    Guid OwnerUserId,
    Guid CanonicalDocumentId,
    Guid? RawResponseFileAssetId,
    Guid? GeneratedExcelFileAssetId,
    string DocumentCode,
    string Provider,
    string Model) : IIntegrationEvent;
