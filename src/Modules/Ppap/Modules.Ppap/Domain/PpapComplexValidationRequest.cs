using DES.Framework.Core.Domain;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapComplexValidationRequest : BaseEntity<Guid>, IHasTenant
{
    public const int ExecutorKeyMaxLength = 128;
    public const int InputFingerprintMaxLength = 64;

    public string TenantId { get; private set; } = default!;
    public Guid SubmissionId { get; private set; }
    public Guid RuleVersionId { get; private set; }
    public Guid RequestEventId { get; private set; }
    public Guid ValidationRunId { get; private set; }
    public string ExecutorKey { get; private set; } = default!;
    public string InputFingerprint { get; private set; } = default!;
    public DateTime RequestedAtUtc { get; private set; }

    private PpapComplexValidationRequest() { }

    public static PpapComplexValidationRequest Create(
        string tenantId,
        Guid submissionId,
        Guid ruleVersionId,
        Guid requestEventId,
        Guid validationRunId,
        string executorKey,
        string inputFingerprint,
        DateTime requestedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ValidateId(submissionId, nameof(submissionId));
        ValidateId(ruleVersionId, nameof(ruleVersionId));
        ValidateId(requestEventId, nameof(requestEventId));
        ValidateId(validationRunId, nameof(validationRunId));
        if (requestedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", nameof(requestedAtUtc));
        return new PpapComplexValidationRequest
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId.Trim(),
            SubmissionId = submissionId,
            RuleVersionId = ruleVersionId,
            RequestEventId = requestEventId,
            ValidationRunId = validationRunId,
            ExecutorKey = Normalize(executorKey, ExecutorKeyMaxLength, nameof(executorKey)),
            InputFingerprint = Normalize(inputFingerprint, InputFingerprintMaxLength, nameof(inputFingerprint)),
            RequestedAtUtc = requestedAtUtc
        };
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty) throw new ArgumentException("ID must not be empty.", parameterName);
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength) throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }
}
