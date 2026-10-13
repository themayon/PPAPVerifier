using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.Ppap.Contracts.Events;

public sealed record PpapProcessingCompletedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string TenantId,
    string CorrelationId,
    string Source,
    Guid JobId,
    Guid SubmissionId,
    Guid ReportId,
    int SubmissionVersion,
    Guid OwnerUserId,
    string PartName,
    string PartNumber) : IIntegrationEvent;
