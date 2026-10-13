using System.Text;
using System.Text.Json;
using DES.Framework.Core.Domain;
using DES.Modules.Rules.Contracts;

namespace DES.Modules.Rules.Domain;

public sealed class RuleEvaluationRun : AggregateRoot<Guid>, IHasTenant
{
    private readonly List<RuleEvaluationResultRecord> _results = [];

    public string TenantId { get; private set; } = default!;
    public Guid TemplateVersionId { get; private set; }
    public Guid SubjectId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string InputSchemaVersion { get; private set; } = default!;
    public EvaluationRunStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyCollection<RuleEvaluationResultRecord> Results => _results.AsReadOnly();

    private RuleEvaluationRun() { }

    public static RuleEvaluationRun Start(
        string tenantId,
        Guid templateVersionId,
        Guid subjectId,
        Guid ownerUserId,
        string inputSchemaVersion,
        DateTime utcNow)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (templateVersionId == Guid.Empty) throw new ArgumentException("Template version is required.", nameof(templateVersionId));
        if (subjectId == Guid.Empty) throw new ArgumentException("Subject is required.", nameof(subjectId));
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner is required.", nameof(ownerUserId));
        ArgumentException.ThrowIfNullOrWhiteSpace(inputSchemaVersion);
        string schemaVersion = inputSchemaVersion.Trim();
        if (schemaVersion.Length > 32) throw new ArgumentOutOfRangeException(nameof(inputSchemaVersion));
        RequireUtc(utcNow, nameof(utcNow));

        return new RuleEvaluationRun
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            TemplateVersionId = templateVersionId,
            SubjectId = subjectId,
            OwnerUserId = ownerUserId,
            InputSchemaVersion = schemaVersion,
            Status = EvaluationRunStatus.Running,
            CreatedAtUtc = utcNow,
            Version = 1,
        };
    }

    public void AppendResult(
        Guid ruleDefinitionId,
        Guid ruleVersionId,
        string? documentCode,
        RuleEvaluationStatus status,
        string? reason,
        string traceJson,
        DateTime evaluatedAtUtc)
    {
        EnsureRunning();
        string? normalizedCode = string.IsNullOrWhiteSpace(documentCode)
            ? null
            : TemplateDocumentRequirement.NormalizeDocumentCode(documentCode);
        if (_results.Any(x => x.RuleVersionId == ruleVersionId && string.Equals(x.DocumentCode, normalizedCode, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("A result already exists for the rule version and document scope.");
        }

        _results.Add(RuleEvaluationResultRecord.Create(
            Id,
            ruleDefinitionId,
            ruleVersionId,
            normalizedCode,
            status,
            reason,
            traceJson,
            evaluatedAtUtc));
        Version++;
    }

    public void Complete(DateTime utcNow)
    {
        EnsureRunning();
        RequireUtc(utcNow, nameof(utcNow));
        if (utcNow < CreatedAtUtc) throw new ArgumentOutOfRangeException(nameof(utcNow));

        Status = _results.Count > 0 && _results.All(x => x.Status == RuleEvaluationStatus.Error)
            ? EvaluationRunStatus.Failed
            : _results.Any(x => x.Status is RuleEvaluationStatus.Error or RuleEvaluationStatus.NotEvaluated)
                ? EvaluationRunStatus.PartiallyCompleted
                : EvaluationRunStatus.Completed;
        CompletedAtUtc = utcNow;
        Version++;
    }

    private void EnsureRunning()
    {
        if (Status != EvaluationRunStatus.Running)
        {
            throw new InvalidOperationException("A terminal evaluation run cannot be changed.");
        }
    }

    private static void RequireUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }
}

public sealed class RuleEvaluationResultRecord : BaseEntity<Guid>
{
    internal const int MaxTraceBytes = 64 * 1024;

    public Guid RuleEvaluationRunId { get; private set; }
    public Guid RuleDefinitionId { get; private set; }
    public Guid RuleVersionId { get; private set; }
    public string? DocumentCode { get; private set; }
    public RuleEvaluationStatus Status { get; private set; }
    public string? Reason { get; private set; }
    public string TraceJson { get; private set; } = default!;
    public DateTime EvaluatedAtUtc { get; private set; }

    private RuleEvaluationResultRecord() { }

    internal static RuleEvaluationResultRecord Create(
        Guid runId,
        Guid ruleDefinitionId,
        Guid ruleVersionId,
        string? documentCode,
        RuleEvaluationStatus status,
        string? reason,
        string traceJson,
        DateTime evaluatedAtUtc)
    {
        if (runId == Guid.Empty) throw new ArgumentException("Evaluation run is required.", nameof(runId));
        if (ruleDefinitionId == Guid.Empty) throw new ArgumentException("Rule definition is required.", nameof(ruleDefinitionId));
        if (ruleVersionId == Guid.Empty) throw new ArgumentException("Rule version is required.", nameof(ruleVersionId));
        if (evaluatedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", nameof(evaluatedAtUtc));
        string? normalizedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (normalizedReason?.Length > 1000) normalizedReason = normalizedReason[..1000];
        ArgumentException.ThrowIfNullOrWhiteSpace(traceJson);
        if (Encoding.UTF8.GetByteCount(traceJson) > MaxTraceBytes) throw new ArgumentOutOfRangeException(nameof(traceJson));
        using JsonDocument trace = JsonDocument.Parse(traceJson);
        if (trace.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("Trace must be a JSON array.", nameof(traceJson));

        return new RuleEvaluationResultRecord
        {
            Id = Guid.CreateVersion7(),
            RuleEvaluationRunId = runId,
            RuleDefinitionId = ruleDefinitionId,
            RuleVersionId = ruleVersionId,
            DocumentCode = documentCode,
            Status = status,
            Reason = normalizedReason,
            TraceJson = JsonSerializer.Serialize(trace.RootElement),
            EvaluatedAtUtc = evaluatedAtUtc,
        };
    }
}
