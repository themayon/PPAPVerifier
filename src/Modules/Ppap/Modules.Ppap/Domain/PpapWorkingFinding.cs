using System.Text;
using System.Text.Json;
using DES.Framework.Core.Domain;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapWorkingFinding : BaseEntity<Guid>, IHasTenant
{
    public const int StableRuleCodeMaxLength = 64;
    public const int RuleNameMaxLength = 200;
    public const int MessageMaxLength = 1000;
    public const int InputFingerprintMaxLength = 128;
    public const int MaximumEvidenceBytes = 64 * 1024;

    public string TenantId { get; private set; } = default!;
    public Guid SubmissionId { get; private set; }
    public Guid ValidationRunId { get; private set; }
    public Guid RuleVersionId { get; private set; }
    public Guid? SubmissionDocumentId { get; private set; }
    public Guid? DocumentRevisionId { get; private set; }
    public string StableRuleCode { get; private set; } = default!;
    public string RuleName { get; private set; } = default!;
    public WorkingFindingStatus Status { get; private set; }
    public string Message { get; private set; } = default!;
    public string InputFingerprint { get; private set; } = default!;
    public string EvidenceJson { get; private set; } = default!;
    public bool IsCurrent { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? SupersededAtUtc { get; private set; }

    private PpapWorkingFinding() { }

    public static PpapWorkingFinding Create(
        string tenantId,
        Guid submissionId,
        Guid validationRunId,
        Guid ruleVersionId,
        Guid? submissionDocumentId,
        Guid? documentRevisionId,
        string stableRuleCode,
        string ruleName,
        WorkingFindingStatus status,
        string message,
        string inputFingerprint,
        string evidenceJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ValidateId(submissionId, nameof(submissionId));
        ValidateId(validationRunId, nameof(validationRunId));
        ValidateId(ruleVersionId, nameof(ruleVersionId));
        ValidateOptionalId(submissionDocumentId, nameof(submissionDocumentId));
        ValidateOptionalId(documentRevisionId, nameof(documentRevisionId));
        if (submissionDocumentId.HasValue != documentRevisionId.HasValue)
        {
            throw new ArgumentException("Document-scoped findings require both document and revision identifiers.", nameof(documentRevisionId));
        }
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        ValidateEvidence(evidenceJson);

        return new PpapWorkingFinding
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId.Trim(),
            SubmissionId = submissionId,
            ValidationRunId = validationRunId,
            RuleVersionId = ruleVersionId,
            SubmissionDocumentId = submissionDocumentId,
            DocumentRevisionId = documentRevisionId,
            StableRuleCode = Normalize(stableRuleCode, StableRuleCodeMaxLength, nameof(stableRuleCode)),
            RuleName = Normalize(ruleName, RuleNameMaxLength, nameof(ruleName)),
            Status = status,
            Message = Normalize(message, MessageMaxLength, nameof(message)),
            InputFingerprint = Normalize(inputFingerprint, InputFingerprintMaxLength, nameof(inputFingerprint)),
            EvidenceJson = evidenceJson,
            IsCurrent = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Supersede(DateTime occurredOnUtc)
    {
        if (occurredOnUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", nameof(occurredOnUtc));
        if (!IsCurrent) return;
        IsCurrent = false;
        SupersededAtUtc = occurredOnUtc;
    }

    private static void ValidateEvidence(string evidenceJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceJson);
        if (Encoding.UTF8.GetByteCount(evidenceJson) > MaximumEvidenceBytes) throw new ArgumentOutOfRangeException(nameof(evidenceJson));
        using JsonDocument document = JsonDocument.Parse(evidenceJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("Evidence must be a JSON array.", nameof(evidenceJson));
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty) throw new ArgumentException("ID must not be empty.", parameterName);
    }

    private static void ValidateOptionalId(Guid? value, string parameterName)
    {
        if (value == Guid.Empty) throw new ArgumentException("ID must not be empty when supplied.", parameterName);
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength) throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }
}
