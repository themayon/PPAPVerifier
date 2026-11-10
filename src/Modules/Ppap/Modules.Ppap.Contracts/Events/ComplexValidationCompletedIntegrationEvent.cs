using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.Ppap.Contracts.Events;

public sealed record ComplexValidationEvidence(
    string DocumentCode,
    Guid SubmissionDocumentId,
    Guid DocumentRevisionId,
    string ArtifactKind,
    int? PageNumber,
    decimal? X,
    decimal? Y,
    decimal? Width,
    decimal? Height,
    string? SheetName,
    string? StartCell,
    string? EndCell,
    string? CanonicalPath,
    string? ObservedValue,
    string? ExpectedValue);

public sealed record ComplexValidationCompletedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string TenantId,
    string CorrelationId,
    string Source,
    Guid RequestEventId,
    Guid SubmissionId,
    Guid ValidationRunId,
    Guid RuleVersionId,
    string StableRuleCode,
    string RuleName,
    string ExecutorKey,
    string ExecutorVersion,
    string InputFingerprint,
    string Status,
    string? Message,
    long DurationMilliseconds,
    IReadOnlyList<ComplexValidationEvidence> Evidence) : IIntegrationEvent;
