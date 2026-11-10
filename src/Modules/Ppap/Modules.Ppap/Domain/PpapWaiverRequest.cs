using System.Net;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapWaiverRequest : BaseEntity<Guid>, IHasTenant
{
    public const int ReasonMaxLength = 1000;

    public string TenantId { get; private set; } = default!;
    public Guid SubmissionId { get; private set; }
    public Guid WorkingFindingId { get; private set; }
    public string FindingFingerprint { get; private set; } = default!;
    public Guid RequestedByUserId { get; private set; }
    public string RequestReason { get; private set; } = default!;
    public WaiverRequestStatus Status { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? ReviewReason { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public long Version { get; private set; }

    private PpapWaiverRequest() { }

    public static PpapWaiverRequest Create(
        string tenantId,
        Guid submissionId,
        Guid workingFindingId,
        string findingFingerprint,
        Guid requestedByUserId,
        string reason,
        DateTime occurredOnUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ValidateId(submissionId, nameof(submissionId));
        ValidateId(workingFindingId, nameof(workingFindingId));
        ValidateId(requestedByUserId, nameof(requestedByUserId));
        ValidateUtc(occurredOnUtc, nameof(occurredOnUtc));
        return new PpapWaiverRequest
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId.Trim(),
            SubmissionId = submissionId,
            WorkingFindingId = workingFindingId,
            FindingFingerprint = Normalize(findingFingerprint, PpapWorkingFinding.InputFingerprintMaxLength, nameof(findingFingerprint)),
            RequestedByUserId = requestedByUserId,
            RequestReason = Normalize(reason, ReasonMaxLength, nameof(reason)),
            Status = WaiverRequestStatus.Pending,
            RequestedAtUtc = occurredOnUtc
        };
    }

    public void Decide(bool approve, Guid reviewerUserId, string reason, long expectedVersion, DateTime occurredOnUtc)
    {
        EnsureVersion(expectedVersion);
        if (Status != WaiverRequestStatus.Pending) throw Conflict("Only a pending waiver request can be reviewed.");
        ValidateId(reviewerUserId, nameof(reviewerUserId));
        ValidateUtc(occurredOnUtc, nameof(occurredOnUtc));
        ReviewedByUserId = reviewerUserId;
        ReviewReason = Normalize(reason, ReasonMaxLength, nameof(reason));
        Status = approve ? WaiverRequestStatus.Approved : WaiverRequestStatus.Rejected;
        ReviewedAtUtc = occurredOnUtc;
        Version++;
    }

    public void Withdraw(Guid requesterUserId, long expectedVersion)
    {
        EnsureVersion(expectedVersion);
        if (Status != WaiverRequestStatus.Pending) throw Conflict("Only a pending waiver request can be withdrawn.");
        if (RequestedByUserId != requesterUserId) throw Conflict("Only the requester can withdraw this waiver request.");
        Status = WaiverRequestStatus.Withdrawn;
        Version++;
    }

    public void Supersede()
    {
        if (Status == WaiverRequestStatus.Superseded) return;
        Status = WaiverRequestStatus.Superseded;
        Version++;
    }

    private void EnsureVersion(long expectedVersion)
    {
        if (Version != expectedVersion) throw Conflict("The waiver request was modified by another operation.");
    }

    private static void ValidateId(Guid value, string parameterName)
    {
        if (value == Guid.Empty) throw new ArgumentException("ID must not be empty.", parameterName);
    }

    private static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", parameterName);
    }

    private static string Normalize(string value, int maximumLength, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > maximumLength) throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
