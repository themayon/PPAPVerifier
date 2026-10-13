using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.DocumentIntelligence.Contracts.Events;

public sealed record DocumentExtractionFailedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string TenantId,
    string CorrelationId,
    string Source,
    Guid ExtractionAttemptId,
    Guid SubmissionDocumentId,
    Guid OwnerUserId,
    string DocumentCode,
    string Provider,
    string Model,
    string Outcome,
    string ErrorCode) : IIntegrationEvent;
