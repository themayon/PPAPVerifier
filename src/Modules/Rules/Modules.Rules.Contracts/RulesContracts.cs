using DES.Framework.Shared.Persistence;
using Mediator;

namespace DES.Modules.Rules.Contracts;

public sealed class RulesContractsMarker;

public enum RuleCategory { Header, Row, Document, CrossDocument, Drawing, ModelInference, Special, Custom }
public enum RuleLifecycle { Draft, InReview, Published, Retired }
public enum RuleEvaluationStatus { Passed, Failed, NotEvaluated, Error }
public enum EvaluationRunStatus { Running, Completed, PartiallyCompleted, Failed }
public enum TemplateDocumentDisposition { Submit, Retain, Available }

public sealed record RuleVersionMetadataDto(Guid Id, int VersionNumber, RuleLifecycle Status, DateTime CreatedAtUtc, DateTime? PublishedAtUtc, DateTime? RetiredAtUtc, Guid CreatedByUserId, string? AstJson = null, string? OutcomeJson = null);
public sealed record RuleDefinitionSummaryDto(Guid Id, string StableCode, string Name, string? Description, RuleCategory Category, RuleLifecycle Lifecycle, Guid? CurrentPublishedVersionId, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, long Version, string? LatestOutcomeJson = null, int ConditionCount = 0);
public sealed record RuleDefinitionDetailDto(Guid Id, string StableCode, string Name, string? Description, RuleCategory Category, RuleLifecycle Lifecycle, Guid? CurrentPublishedVersionId, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, long Version, IReadOnlyList<RuleVersionMetadataDto> Versions);
public sealed record RuleTraceDto(string Node, string? Operator, string? Left, string? Right, RuleEvaluationStatus Status, string? Reason);
public sealed record RuleEvaluationResultDto(RuleEvaluationStatus Status, string? Reason, IReadOnlyList<RuleTraceDto> Trace);

public sealed record SearchRulesQuery : IQuery<PagedResponse<RuleDefinitionSummaryDto>>, IPagedQuery
{
    public string? Search { get; init; }
    public RuleCategory? Category { get; init; }
    public string? Categories { get; init; }
    public RuleLifecycle? Status { get; init; }
    public string? Severity { get; init; }
    public string? Tag { get; init; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}
public sealed record GetRuleQuery(Guid DefinitionId) : IQuery<RuleDefinitionDetailDto>;
public sealed record CreateRuleCommand(string StableCode, string Name, string? Description, RuleCategory Category, string AstJson, string OutcomeJson) : ICommand<Guid>;
public sealed record UpdateRuleDraftCommand(Guid DefinitionId, Guid VersionId, long ExpectedAggregateVersion, string Name, string? Description, RuleCategory Category, string AstJson, string OutcomeJson) : ICommand;
public sealed record SubmitRuleForReviewCommand(Guid DefinitionId, Guid VersionId, long ExpectedAggregateVersion) : ICommand;
public sealed record PublishRuleCommand(Guid DefinitionId, Guid VersionId, long ExpectedAggregateVersion, bool RetireCurrent = true) : ICommand;
public sealed record RetireRuleCommand(Guid DefinitionId, Guid VersionId, long ExpectedAggregateVersion) : ICommand;
public sealed record ClonePublishedRuleCommand(Guid DefinitionId, Guid VersionId, long ExpectedAggregateVersion) : ICommand<Guid>;
public sealed record TestRuleCommand(string AstJson, IReadOnlyDictionary<string, string> Documents, string? ParametersJson = null) : ICommand<RuleEvaluationResultDto>;

public sealed record TemplateDocumentRequirementDto(Guid Id, string DocumentCode, bool IsRequired, int SortOrder, TemplateDocumentDisposition Disposition);
public sealed record TemplateRuleAssignmentDto(Guid Id, Guid RuleVersionId, string? DocumentCode, string ParametersJson);
public sealed record RuleTemplateVersionDto(Guid Id, int VersionNumber, RuleLifecycle Status, Guid CreatedByUserId, DateTime CreatedAtUtc, DateTime? PublishedAtUtc, DateTime? RetiredAtUtc, IReadOnlyList<TemplateDocumentRequirementDto> DocumentRequirements, IReadOnlyList<TemplateRuleAssignmentDto> RuleAssignments);
public sealed record RuleTemplateSummaryDto(Guid Id, string StableCode, string Name, string? Description, RuleLifecycle Lifecycle, Guid? CurrentPublishedVersionId, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, long Version);
public sealed record RuleTemplateDetailDto(Guid Id, string StableCode, string Name, string? Description, RuleLifecycle Lifecycle, Guid? CurrentPublishedVersionId, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, long Version, IReadOnlyList<RuleTemplateVersionDto> Versions);
public sealed record TemplateDocumentRequirementInput(string DocumentCode, bool IsRequired, int SortOrder, TemplateDocumentDisposition? Disposition = null);
public sealed record TemplateRuleAssignmentInput(Guid RuleVersionId, string? DocumentCode, string ParametersJson);

public sealed record SearchTemplatesQuery : IQuery<PagedResponse<RuleTemplateSummaryDto>>, IPagedQuery
{
    public string? Search { get; init; }
    public RuleLifecycle? Status { get; init; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}

public sealed record GetTemplateQuery(Guid TemplateId) : IQuery<RuleTemplateDetailDto>;
public sealed record GetTemplateVersionQuery(Guid TemplateId, Guid VersionId) : IQuery<RuleTemplateVersionDto>;
public sealed record CreateTemplateCommand(string StableCode, string Name, string? Description) : ICommand<Guid>;
public sealed record ConfigureTemplateDraftCommand(Guid TemplateId, Guid VersionId, long ExpectedAggregateVersion, string Name, string? Description, IReadOnlyList<TemplateDocumentRequirementInput> DocumentRequirements, IReadOnlyList<TemplateRuleAssignmentInput> RuleAssignments) : ICommand;
public sealed record SubmitTemplateForReviewCommand(Guid TemplateId, Guid VersionId, long ExpectedAggregateVersion) : ICommand;
public sealed record PublishTemplateCommand(Guid TemplateId, Guid VersionId, long ExpectedAggregateVersion, bool RetireCurrent = true) : ICommand;
public sealed record RetireTemplateCommand(Guid TemplateId, Guid VersionId, long ExpectedAggregateVersion) : ICommand;
public sealed record ClonePublishedTemplateCommand(Guid TemplateId, Guid VersionId, long ExpectedAggregateVersion) : ICommand<Guid>;

public sealed record RuleEvaluationRequest(
    Guid TemplateVersionId,
    Guid SubjectId,
    Guid OwnerUserId,
    string InputSchemaVersion,
    IReadOnlyDictionary<string, string> Documents);

public interface IRuleEvaluationService
{
    Task<Guid> EvaluateAsync(RuleEvaluationRequest request, CancellationToken cancellationToken = default);
}

public interface IRuleEvaluationResultReader
{
    Task<RuleEvaluationRunDetails> ReadAsync(
        Guid evaluationRunId,
        Guid ownerUserId,
        Guid? subjectId = null,
        CancellationToken cancellationToken = default);
}

public sealed record RuleEvaluationResultDetails(
    Guid Id,
    Guid RuleDefinitionId,
    Guid RuleVersionId,
    Guid TemplateVersionId,
    string StableRuleCode,
    string RuleName,
    string? DocumentCode,
    RuleEvaluationStatus Status,
    string? Reason,
    string? Message,
    string TraceJson,
    DateTime EvaluatedAtUtc);

public sealed record RuleEvaluationRunDetails(
    Guid Id,
    Guid TemplateVersionId,
    Guid SubjectId,
    Guid OwnerUserId,
    string InputSchemaVersion,
    EvaluationRunStatus Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    long Version,
    IReadOnlyList<RuleEvaluationResultDetails> Results);

public sealed record EvaluateTemplateCommand(
    Guid TemplateVersionId,
    Guid SubjectId,
    Guid OwnerUserId,
    string InputSchemaVersion,
    IReadOnlyDictionary<string, string> Documents) : ICommand<Guid>;

public sealed record GetRuleEvaluationQuery(Guid Id) : IQuery<EvaluationRunDto>;

public sealed record EvaluationResultDto(
    Guid Id,
    Guid RuleDefinitionId,
    Guid RuleVersionId,
    string? DocumentCode,
    RuleEvaluationStatus Status,
    string? Reason,
    string TraceJson,
    DateTime EvaluatedAtUtc);

public sealed record EvaluationRunDto(
    Guid Id,
    Guid TemplateVersionId,
    Guid SubjectId,
    Guid OwnerUserId,
    string InputSchemaVersion,
    EvaluationRunStatus Status,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    long Version,
    IReadOnlyList<EvaluationResultDto> Results);
