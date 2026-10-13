using System.Text;
using System.Text.Json;
using DES.Framework.Core.Domain;
using DES.Modules.Ppap.Contracts.v1.Reports;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapFinding : BaseEntity<Guid>, IHasTenant
{
    public const int StableRuleCodeMaxLength = 64;
    public const int RuleNameMaxLength = 200;
    public const int DocumentCodeMaxLength = 32;
    public const int MessageMaxLength = 1000;
    public const int ReasonMaxLength = 1000;
    public const int MaxTraceBytes = 64 * 1024;

    public string TenantId { get; private set; } = default!;
    public Guid SubmissionId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid RuleEvaluationRunId { get; private set; }
    public Guid RuleEvaluationResultId { get; private set; }
    public Guid TemplateVersionId { get; private set; }
    public Guid RuleDefinitionId { get; private set; }
    public Guid RuleVersionId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid? SubmissionDocumentId { get; private set; }
    public Guid? CanonicalDocumentId { get; private set; }
    public Guid? ExtractionAttemptId { get; private set; }
    public string StableRuleCode { get; private set; } = default!;
    public string RuleName { get; private set; } = default!;
    public string? DocumentCode { get; private set; }
    public PpapFindingOutcome Outcome { get; private set; }
    public string? Message { get; private set; }
    public string? Reason { get; private set; }
    public string TraceJson { get; private set; } = default!;
    public DateTime EvaluatedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private PpapFinding() { }

    public static PpapFinding Create(
        string tenantId,
        Guid submissionId,
        Guid jobId,
        Guid ruleEvaluationRunId,
        Guid ruleEvaluationResultId,
        Guid templateVersionId,
        Guid ruleDefinitionId,
        Guid ruleVersionId,
        Guid ownerUserId,
        string stableRuleCode,
        string ruleName,
        string? documentCode,
        PpapFindingOutcome outcome,
        string? message,
        string? reason,
        string traceJson,
        DateTime evaluatedAtUtc,
        Guid? submissionDocumentId = null,
        Guid? canonicalDocumentId = null,
        Guid? extractionAttemptId = null)
    {
        string normalizedTenantId = NormalizeRequired(tenantId, 64, nameof(tenantId));
        ValidateId(submissionId, nameof(submissionId));
        ValidateId(jobId, nameof(jobId));
        ValidateId(ruleEvaluationRunId, nameof(ruleEvaluationRunId));
        ValidateId(ruleEvaluationResultId, nameof(ruleEvaluationResultId));
        ValidateId(templateVersionId, nameof(templateVersionId));
        ValidateId(ruleDefinitionId, nameof(ruleDefinitionId));
        ValidateId(ruleVersionId, nameof(ruleVersionId));
        ValidateId(ownerUserId, nameof(ownerUserId));
        if (!Enum.IsDefined(outcome)) throw new ArgumentOutOfRangeException(nameof(outcome));
        if (submissionDocumentId == Guid.Empty) throw new ArgumentException("Submission document ID must not be empty.", nameof(submissionDocumentId));
        if (canonicalDocumentId == Guid.Empty) throw new ArgumentException("Canonical document ID must not be empty.", nameof(canonicalDocumentId));
        if (extractionAttemptId == Guid.Empty) throw new ArgumentException("Extraction attempt ID must not be empty.", nameof(extractionAttemptId));
        if (evaluatedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Evaluation timestamp must be UTC.", nameof(evaluatedAtUtc));
        if (Encoding.UTF8.GetByteCount(traceJson) > MaxTraceBytes) throw new ArgumentOutOfRangeException(nameof(traceJson));
        using JsonDocument trace = JsonDocument.Parse(traceJson);
        if (trace.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("Trace must be a JSON array.", nameof(traceJson));

        string? normalizedDocumentCode = NormalizeOptional(documentCode, DocumentCodeMaxLength, nameof(documentCode));
        if (normalizedDocumentCode is not null)
        {
            normalizedDocumentCode = normalizedDocumentCode.ToUpperInvariant();
            if (submissionDocumentId is null || canonicalDocumentId is null || extractionAttemptId is null)
            {
                throw new ArgumentException("Document-scoped findings require submission document, canonical document, and extraction attempt correlation.", nameof(documentCode));
            }
        }
        else if (submissionDocumentId is not null || canonicalDocumentId is not null || extractionAttemptId is not null)
        {
            throw new ArgumentException("Cross-document findings cannot carry document correlation.", nameof(documentCode));
        }

        return new PpapFinding
        {
            Id = Guid.CreateVersion7(),
            TenantId = normalizedTenantId,
            SubmissionId = submissionId,
            JobId = jobId,
            RuleEvaluationRunId = ruleEvaluationRunId,
            RuleEvaluationResultId = ruleEvaluationResultId,
            TemplateVersionId = templateVersionId,
            RuleDefinitionId = ruleDefinitionId,
            RuleVersionId = ruleVersionId,
            OwnerUserId = ownerUserId,
            SubmissionDocumentId = submissionDocumentId,
            CanonicalDocumentId = canonicalDocumentId,
            ExtractionAttemptId = extractionAttemptId,
            StableRuleCode = NormalizeRequired(stableRuleCode, StableRuleCodeMaxLength, nameof(stableRuleCode)),
            RuleName = NormalizeRequired(ruleName, RuleNameMaxLength, nameof(ruleName)),
            DocumentCode = normalizedDocumentCode,
            Outcome = outcome,
            Message = NormalizeOptional(message, MessageMaxLength, nameof(message)),
            Reason = NormalizeOptional(reason, ReasonMaxLength, nameof(reason)),
            TraceJson = JsonSerializer.Serialize(trace.RootElement),
            EvaluatedAtUtc = evaluatedAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static void ValidateId(Guid id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID is required.", parameterName);
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
        string normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(parameterName);
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = value.Trim();
        return normalized.Length <= maxLength ? normalized : throw new ArgumentOutOfRangeException(parameterName);
    }
}
