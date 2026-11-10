using DES.Framework.Shared.Persistence;
using Mediator;

namespace DES.Modules.Rules.Contracts;

public sealed class RulesContractsMarker;

public enum RuleCategory { Header, Row, Document, CrossDocument, Drawing, ModelInference, Special, Custom }
public enum RuleLifecycle { Draft, InReview, Published, Retired }
public enum RuleEvaluationStatus { Passed, Failed, NotEvaluated, Error }
public enum EvaluationRunStatus { Running, Completed, PartiallyCompleted, Failed }
public enum TemplateDocumentDisposition { Submit, Retain, Available }
public enum RuleColumnKind { Column, Header }
public enum RuleColumnDataType { Text, Number, Date, Boolean }

public sealed record RuleColumnDto(Guid Id, string Name, string Key, RuleColumnKind Kind, RuleColumnDataType DataType, string? HeaderNames, string? Description, bool IsActive, DateTime CreatedAtUtc, DateTime UpdatedAtUtc, long Version);
public sealed record SearchRuleColumnsQuery : IQuery<PagedResponse<RuleColumnDto>>, IPagedQuery
{
    public string? Search { get; init; }
    public RuleColumnKind? Kind { get; init; }
    public RuleColumnDataType? DataType { get; init; }
    public bool? IsActive { get; init; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 50;
    public string? Sort { get; set; }
}
public sealed record GetRuleColumnQuery(Guid Id) : IQuery<RuleColumnDto>;
public sealed record CreateRuleColumnCommand(string Name, string Key, RuleColumnKind Kind, RuleColumnDataType DataType, string? HeaderNames, string? Description) : ICommand<Guid>;
public sealed record UpdateRuleColumnCommand(Guid Id, long ExpectedVersion, string Name, string Key, RuleColumnDataType DataType, string? HeaderNames, string? Description, bool IsActive) : ICommand;
public sealed record DeleteRuleColumnCommand(Guid Id) : ICommand;

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
public sealed record DeleteRuleCommand(Guid DefinitionId, long ExpectedAggregateVersion) : ICommand;
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

public sealed record DocumentSettingDto(Guid Id, string DocumentCode, string Name, bool IsEnabled, int SortOrder, TemplateDocumentDisposition DefaultDisposition, IReadOnlyList<Guid> RuleDefinitionIds, DateTime UpdatedAtUtc, long Version);
public sealed record GetDocumentSettingsQuery : IQuery<IReadOnlyList<DocumentSettingDto>>;
public sealed record UpdateDocumentSettingCommand(Guid Id, long ExpectedVersion, string Name, bool IsEnabled, int SortOrder, TemplateDocumentDisposition DefaultDisposition, IReadOnlyList<Guid> RuleDefinitionIds) : ICommand<DocumentSettingDto>;

public sealed record RuleLookupDto(Guid Id, string StableCode, string Name, string? Description, RuleCategory Category, RuleLifecycle Lifecycle, bool IsLive, string? LatestOutcomeJson);
public sealed record GetRuleLookupQuery : IQuery<IReadOnlyList<RuleLookupDto>>;

public sealed record TemplateSetupDocumentInput(string DocumentCode, bool IsEnabled, TemplateDocumentDisposition Disposition, int SortOrder);
public sealed record TemplateSetupRuleInput(string DocumentCode, Guid RuleDefinitionId, bool IsEnabled);
public sealed record TemplateSetupDocumentDto(string DocumentCode, bool IsEnabled, TemplateDocumentDisposition Disposition, int SortOrder);
public sealed record TemplateSetupRuleDto(string DocumentCode, Guid RuleDefinitionId, bool IsEnabled);
public sealed record TemplateSetupDto(Guid Id, string StableCode, string Name, string? Description, RuleLifecycle Lifecycle, Guid? CurrentPublishedVersionId, DateTime UpdatedAtUtc, long Version, IReadOnlyList<TemplateSetupDocumentDto> Documents, IReadOnlyList<TemplateSetupRuleDto> Rules);
public sealed record TemplateSetupSummaryDto(Guid Id, string StableCode, string Name, string? Description, RuleLifecycle Lifecycle, Guid? CurrentPublishedVersionId, int EnabledDocumentCount, int DocumentCount, int EnabledRuleCount, int RuleCount, DateTime UpdatedAtUtc, long Version);
public sealed record TemplateSetupSaveResultDto(Guid TemplateId, Guid PublishedVersionId, long Version, IReadOnlyList<Guid> SkippedRuleDefinitionIds);
public sealed record SearchTemplateSetupsQuery : IQuery<PagedResponse<TemplateSetupSummaryDto>>, IPagedQuery
{
    public string? Search { get; init; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 50;
    public string? Sort { get; set; }
}
public sealed record GetTemplateSetupQuery(Guid TemplateId) : IQuery<TemplateSetupDto>;
public sealed record CreateTemplateFromDocumentSettingsCommand(string StableCode, string Name, string? Description) : ICommand<TemplateSetupSaveResultDto>;
public sealed record SaveTemplateSetupCommand(Guid TemplateId, long ExpectedVersion, string Name, string? Description, IReadOnlyList<TemplateSetupDocumentInput> Documents, IReadOnlyList<TemplateSetupRuleInput> Rules) : ICommand<TemplateSetupSaveResultDto>;

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

public sealed record ComplexValidationPlanItem(
    Guid RuleVersionId,
    string StableRuleCode,
    string RuleName,
    RuleCategory Category,
    string ExecutorKey,
    string ParametersJson,
    IReadOnlyList<string> RequiredDocumentCodes);

public interface IComplexValidationPlanReader
{
    Task<IReadOnlyList<ComplexValidationPlanItem>> ReadAsync(
        Guid templateVersionId,
        CancellationToken cancellationToken = default);
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
