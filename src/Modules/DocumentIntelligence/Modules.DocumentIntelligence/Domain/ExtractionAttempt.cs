using System.Net;
using System.Text;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;

namespace DES.Modules.DocumentIntelligence.Domain;

public sealed class ExtractionAttempt : AggregateRoot<Guid>, IHasTenant
{
    public const int TenantIdMaxLength = 64;
    public const int DocumentCodeMaxLength = 32;
    public const int IdempotencyKeyMaxLength = 200;
    public const int ProviderMaxLength = 100;
    public const int ModelMaxLength = 200;
    public const int ExtractorVersionMaxLength = 100;
    public const int ProviderRequestIdMaxLength = 200;
    public const int ErrorCodeMaxLength = 100;
    public const int ErrorMessageMaxLength = 1000;
    public const int ClaimedByMaxLength = 200;

    public string TenantId { get; private set; } = default!;
    public Guid OwnerUserId { get; private set; }
    public Guid SubmissionDocumentId { get; private set; }
    public Guid SourceFileAssetId { get; private set; }
    public string DocumentCode { get; private set; } = default!;
    public string SourceSha256 { get; private set; } = default!;
    public string IdempotencyKey { get; private set; } = default!;
    public string Provider { get; private set; } = default!;
    public string Model { get; private set; } = default!;
    public string ExtractorVersion { get; private set; } = default!;
    public ExtractionAttemptStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? ProviderRequestId { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ClaimedAtUtc { get; private set; }
    public string? ClaimedBy { get; private set; }
    public DateTime? ClaimedUntilUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime? StartedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid? CompletionEventId { get; private set; }
    public DateTime? CompletionEventPublishedAtUtc { get; private set; }
    public Guid? FailureEventId { get; private set; }
    public DateTime? FailureEventPublishedAtUtc { get; private set; }
    public int Version { get; private set; }

    private ExtractionAttempt() { }

    public static ExtractionAttempt Create(
        string tenantId,
        Guid ownerUserId,
        Guid submissionDocumentId,
        Guid sourceFileAssetId,
        string documentCode,
        string sourceSha256,
        string idempotencyKey,
        string provider,
        string model,
        string extractorVersion)
    {
        string normalizedTenantId = Required(tenantId, TenantIdMaxLength, nameof(tenantId));
        EnsureId(ownerUserId, nameof(ownerUserId));
        EnsureId(submissionDocumentId, nameof(submissionDocumentId));
        EnsureId(sourceFileAssetId, nameof(sourceFileAssetId));
        string normalizedDocumentCode = Required(documentCode, DocumentCodeMaxLength, nameof(documentCode));
        string normalizedHash = string.Concat(Required(sourceSha256, 64, nameof(sourceSha256)).Select(char.ToLowerInvariant));
        if (normalizedHash.Length != 64 || normalizedHash.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException("Source SHA-256 must contain exactly 64 hexadecimal characters.", nameof(sourceSha256));
        }

        string normalizedIdempotencyKey = Required(idempotencyKey, IdempotencyKeyMaxLength, nameof(idempotencyKey));
        string normalizedProvider = Required(provider, ProviderMaxLength, nameof(provider));
        string normalizedModel = Required(model, ModelMaxLength, nameof(model));
        string normalizedExtractorVersion = Required(extractorVersion, ExtractorVersionMaxLength, nameof(extractorVersion));
        DateTime now = DateTime.UtcNow;
        return new ExtractionAttempt
        {
            Id = Guid.CreateVersion7(),
            TenantId = normalizedTenantId,
            OwnerUserId = ownerUserId,
            SubmissionDocumentId = submissionDocumentId,
            SourceFileAssetId = sourceFileAssetId,
            DocumentCode = normalizedDocumentCode,
            SourceSha256 = normalizedHash,
            IdempotencyKey = normalizedIdempotencyKey,
            Provider = normalizedProvider,
            Model = normalizedModel,
            ExtractorVersion = normalizedExtractorVersion,
            Status = ExtractionAttemptStatus.Pending,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void Claim(string? claimedBy = null, DateTime? claimedUntilUtc = null)
    {
        EnsureStatus(ExtractionAttemptStatus.Pending, "claim");
        DateTime now = DateTime.UtcNow;
        Status = ExtractionAttemptStatus.Claimed;
        AttemptCount = checked(AttemptCount + 1);
        ClaimedAtUtc = now;
        ClaimedBy = claimedBy is null ? null : Required(claimedBy, ClaimedByMaxLength, nameof(claimedBy));
        if (claimedUntilUtc is not null && (claimedUntilUtc.Value.Kind != DateTimeKind.Utc || claimedUntilUtc <= now))
        {
            throw new ArgumentOutOfRangeException(nameof(claimedUntilUtc));
        }
        ClaimedUntilUtc = claimedUntilUtc;
        RecordMutation();
    }

    public void RenewLease(string claimedBy, DateTime claimedUntilUtc)
    {
        EnsureStatus(ExtractionAttemptStatus.Claimed, "renew lease");
        string normalizedClaimedBy = Required(claimedBy, ClaimedByMaxLength, nameof(claimedBy));
        if (!string.Equals(ClaimedBy, normalizedClaimedBy, StringComparison.Ordinal))
        {
            throw Conflict("Cannot renew an extraction lease owned by another worker.");
        }
        if (claimedUntilUtc.Kind != DateTimeKind.Utc || claimedUntilUtc <= DateTime.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(claimedUntilUtc));
        }

        ClaimedUntilUtc = claimedUntilUtc;
        RecordMutation();
    }

    public void MarkSubmitted(string providerRequestId)
    {
        string normalized = Required(providerRequestId, ProviderRequestIdMaxLength, nameof(providerRequestId));
        if (Status == ExtractionAttemptStatus.Submitted && string.Equals(ProviderRequestId, normalized, StringComparison.Ordinal))
        {
            return;
        }

        EnsureStatus(ExtractionAttemptStatus.Claimed, "mark submitted");
        Status = ExtractionAttemptStatus.Submitted;
        ProviderRequestId = normalized;
        SubmittedAtUtc = DateTime.UtcNow;
        RecordMutation();
    }

    public void MarkProcessing()
    {
        if (Status == ExtractionAttemptStatus.Processing)
        {
            return;
        }

        EnsureStatus(ExtractionAttemptStatus.Submitted, "mark processing");
        Status = ExtractionAttemptStatus.Processing;
        StartedAtUtc = DateTime.UtcNow;
        RecordMutation();
    }

    public void Succeed()
    {
        if (Status == ExtractionAttemptStatus.Succeeded)
        {
            return;
        }

        EnsureActive("succeed");
        Complete(ExtractionAttemptStatus.Succeeded, null, null);
    }

    public void MarkInvalidDocument(string errorCode, string errorMessage)
    {
        CompleteWithError(ExtractionAttemptStatus.InvalidDocument, errorCode, errorMessage, "mark invalid");
    }

    public void Fail(string errorCode, string errorMessage)
    {
        CompleteWithError(ExtractionAttemptStatus.Failed, errorCode, errorMessage, "fail");
    }

    public void MarkTimedOut(string errorCode, string errorMessage)
    {
        CompleteWithError(ExtractionAttemptStatus.TimedOut, errorCode, errorMessage, "time out");
    }

    public void DeadLetterExhausted(string errorCode, string errorMessage)
    {
        if (Status is not (ExtractionAttemptStatus.Pending or ExtractionAttemptStatus.Claimed))
        {
            throw Conflict($"Cannot dead letter an exhausted extraction attempt in status {Status}.");
        }

        Complete(ExtractionAttemptStatus.DeadLettered, SanitizeErrorCode(errorCode), SanitizeErrorMessage(errorMessage));
    }

    public void DeadLetter(string errorCode, string errorMessage)
    {
        string normalizedCode = SanitizeErrorCode(errorCode);
        string normalizedMessage = SanitizeErrorMessage(errorMessage);
        if (Status == ExtractionAttemptStatus.DeadLettered && ErrorCode == normalizedCode && ErrorMessage == normalizedMessage)
        {
            return;
        }
        if (Status is not (ExtractionAttemptStatus.Failed or ExtractionAttemptStatus.TimedOut))
        {
            throw Conflict($"Cannot dead letter an extraction attempt in status {Status}.");
        }

        Complete(ExtractionAttemptStatus.DeadLettered, normalizedCode, normalizedMessage);
    }

    public void Requeue()
    {
        if (Status is not (ExtractionAttemptStatus.Failed or ExtractionAttemptStatus.TimedOut))
        {
            throw Conflict($"Cannot requeue an extraction attempt in status {Status}.");
        }

        Status = ExtractionAttemptStatus.Pending;
        ProviderRequestId = null;
        ErrorCode = null;
        ErrorMessage = null;
        ClaimedAtUtc = null;
        ClaimedBy = null;
        ClaimedUntilUtc = null;
        SubmittedAtUtc = null;
        StartedAtUtc = null;
        CompletedAtUtc = null;
        RecordMutation();
    }

    public bool MarkCompletionEventPublished(Guid eventId)
    {
        EnsureStatus(ExtractionAttemptStatus.Succeeded, "publish completion event");
        if (CompletionEventPublishedAtUtc is not null)
        {
            return false;
        }
        EnsureId(eventId, nameof(eventId));
        CompletionEventId = eventId;
        CompletionEventPublishedAtUtc = DateTime.UtcNow;
        RecordMutation();
        return true;
    }

    public bool MarkFailureEventPublished(Guid eventId)
    {
        if (Status is not (ExtractionAttemptStatus.InvalidDocument or ExtractionAttemptStatus.DeadLettered))
        {
            throw Conflict($"Cannot publish a failure event for an extraction attempt in status {Status}.");
        }
        if (FailureEventPublishedAtUtc is not null)
        {
            return false;
        }
        EnsureId(eventId, nameof(eventId));
        FailureEventId = eventId;
        FailureEventPublishedAtUtc = DateTime.UtcNow;
        RecordMutation();
        return true;
    }

    private void CompleteWithError(ExtractionAttemptStatus terminalStatus, string errorCode, string errorMessage, string action)
    {
        string normalizedCode = SanitizeErrorCode(errorCode);
        string normalizedMessage = SanitizeErrorMessage(errorMessage);
        if (Status == terminalStatus && ErrorCode == normalizedCode && ErrorMessage == normalizedMessage)
        {
            return;
        }

        EnsureActive(action);
        Complete(terminalStatus, normalizedCode, normalizedMessage);
    }

    private void Complete(ExtractionAttemptStatus status, string? errorCode, string? errorMessage)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CompletedAtUtc = DateTime.UtcNow;
        ClaimedBy = null;
        ClaimedUntilUtc = null;
        RecordMutation();
    }

    private void EnsureActive(string action)
    {
        if (Status is not (ExtractionAttemptStatus.Claimed or ExtractionAttemptStatus.Submitted or ExtractionAttemptStatus.Processing))
        {
            throw Conflict($"Cannot {action} an extraction attempt in status {Status}.");
        }
    }

    private void EnsureStatus(ExtractionAttemptStatus requiredStatus, string action)
    {
        if (Status != requiredStatus)
        {
            throw Conflict($"Cannot {action} an extraction attempt in status {Status}.");
        }
    }

    private void RecordMutation()
    {
        Version = checked(Version + 1);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string Required(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }
        string normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
        return normalized;
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }

    private static string SanitizeErrorCode(string errorCode)
    {
        string value = Required(errorCode, ErrorCodeMaxLength * 2, nameof(errorCode));
        string normalized = new(value
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .Take(ErrorCodeMaxLength)
            .ToArray());
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Error code must contain a letter or digit.", nameof(errorCode));
        }
        return normalized;
    }

    private static string SanitizeErrorMessage(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        StringBuilder sanitized = new(errorMessage.Length);
        bool pendingWhitespace = false;
        foreach (char character in errorMessage.Trim())
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                pendingWhitespace = sanitized.Length > 0;
                continue;
            }
            if (pendingWhitespace && sanitized.Length < ErrorMessageMaxLength)
            {
                sanitized.Append(' ');
            }
            if (sanitized.Length < ErrorMessageMaxLength)
            {
                sanitized.Append(character);
            }
            pendingWhitespace = false;
            if (sanitized.Length == ErrorMessageMaxLength)
            {
                break;
            }
        }

        string normalized = sanitized.ToString().Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Error message is required.", nameof(errorMessage));
        }
        return normalized;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
