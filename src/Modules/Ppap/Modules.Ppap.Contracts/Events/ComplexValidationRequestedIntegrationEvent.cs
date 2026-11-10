using DES.Framework.Eventing.Abstractions;

namespace DES.Modules.Ppap.Contracts.Events;

public sealed record ComplexValidationDocumentInput(
    Guid SubmissionDocumentId,
    Guid DocumentRevisionId,
    string DocumentCode,
    string SourceHash,
    string CanonicalSchemaVersion,
    string CanonicalJson);

public sealed record ComplexValidationRequestedIntegrationEvent(
    Guid Id,
    DateTime OccurredOnUtc,
    string TenantId,
    string CorrelationId,
    string Source,
    Guid SubmissionId,
    Guid ValidationRunId,
    Guid RuleVersionId,
    string StableRuleCode,
    string RuleName,
    string ExecutorKey,
    string ParametersJson,
    string InputFingerprint,
    Guid OwnerUserId,
    IReadOnlyList<ComplexValidationDocumentInput> Documents) : IIntegrationEvent;
