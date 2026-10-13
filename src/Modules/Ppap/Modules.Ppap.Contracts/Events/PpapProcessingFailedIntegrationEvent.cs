using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.Ppap.Contracts.Events;

public sealed record PpapProcessingFailedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string TenantId,
    string CorrelationId,
    string Source,
    Guid JobId,
    Guid SubmissionId,
    int SubmissionVersion,
    Guid OwnerUserId,
    string PartName,
    string PartNumber,
    string ErrorCode,
    string ErrorMessage) : IIntegrationEvent;
