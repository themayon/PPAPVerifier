using DES.Framework.Core.Domain;

namespace DES.Modules.Ppap.Domain;

public enum PpapFindingTargetType
{
    Automated = 0,
    Manual = 1
}

public sealed class PpapFindingWaiver : BaseEntity<Guid>, IHasTenant
{
    public const int ReasonMaxLength = 1000;

    public string TenantId { get; private set; } = default!;
    public Guid ReportId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public PpapFindingTargetType TargetType { get; private set; }
    public Guid TargetFindingId { get; private set; }
    public bool IsActive { get; private set; }
    public Guid ApprovedByUserId { get; private set; }
    public string Reason { get; private set; } = default!;
    public DateTime ApprovedAtUtc { get; private set; }

    private PpapFindingWaiver() { }

    public static PpapFindingWaiver Create(
        string tenantId,
        Guid reportId,
        Guid submissionId,
        Guid jobId,
        Guid ownerUserId,
        PpapFindingTargetType targetType,
        Guid targetFindingId,
        Guid approvedByUserId,
        string reason,
        DateTime approvedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!Enum.IsDefined(targetType)) throw new ArgumentOutOfRangeException(nameof(targetType));
        ValidateId(reportId, nameof(reportId));
        ValidateId(submissionId, nameof(submissionId));
        ValidateId(jobId, nameof(jobId));
        ValidateId(ownerUserId, nameof(ownerUserId));
        ValidateId(targetFindingId, nameof(targetFindingId));
        ValidateId(approvedByUserId, nameof(approvedByUserId));
        if (approvedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Approval timestamp must be UTC.", nameof(approvedAtUtc));

        return new PpapFindingWaiver
        {
            Id = Guid.CreateVersion7(),
            TenantId = NormalizeRequired(tenantId, 64, nameof(tenantId)),
            ReportId = reportId,
            SubmissionId = submissionId,
            JobId = jobId,
            OwnerUserId = ownerUserId,
            TargetType = targetType,
            TargetFindingId = targetFindingId,
            IsActive = true,
            ApprovedByUserId = approvedByUserId,
            Reason = NormalizeRequired(reason, ReasonMaxLength, nameof(reason)),
            ApprovedAtUtc = approvedAtUtc
        };
    }

    public void Deactivate()
    {
        if (!IsActive) throw new InvalidOperationException("Only an active waiver can be revoked.");
        IsActive = false;
    }

    private static void ValidateId(Guid id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID must not be empty.", parameterName);
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }
}
