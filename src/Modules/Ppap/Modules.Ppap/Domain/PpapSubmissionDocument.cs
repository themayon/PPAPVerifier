using System.Net;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapSubmissionDocument : BaseEntity<Guid>, IHasTenant
{
    public const int DocumentCodeMaxLength = 16;
    public const int NotApplicableReasonMaxLength = 500;
    public const int ExtractionFailureCodeMaxLength = 100;

    public string TenantId { get; private set; } = default!;
    public Guid SubmissionId { get; private set; }
    public string DocumentCode { get; private set; } = default!;
    public bool IsRequired { get; private set; }
    public SubmissionDocumentDisposition Disposition { get; private set; }
    public Guid? FileAssetId { get; private set; }
    public string? NotApplicableReason { get; private set; }
    public SubmissionDocumentStatus Status { get; private set; }
    public Guid? ExtractionRequestEventId { get; private set; }
    public DateTime? ExtractionRequestPublishedAtUtc { get; private set; }
    public Guid? ExtractionAttemptId { get; private set; }
    public DocumentExtractionTerminalStatus? ExtractionTerminalStatus { get; private set; }
    public Guid? CanonicalDocumentId { get; private set; }
    public Guid? RawResponseFileAssetId { get; private set; }
    public Guid? GeneratedExcelFileAssetId { get; private set; }
    public string? ExtractionFailureCode { get; private set; }
    public DateTime? ExtractionCompletedAtUtc { get; private set; }
    public DateTime? ExtractionFailedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    private PpapSubmissionDocument() { }

    internal static PpapSubmissionDocument Create(
        string tenantId,
        Guid submissionId,
        string documentCode,
        SubmissionDocumentDisposition disposition)
    {
        if (!Enum.IsDefined(disposition)) throw new ArgumentOutOfRangeException(nameof(disposition));
        return new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            SubmissionId = submissionId,
            DocumentCode = documentCode,
            Disposition = disposition,
            IsRequired = disposition != SubmissionDocumentDisposition.Retain,
            Status = SubmissionDocumentStatus.PendingUpload,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    internal bool MarkNotApplicable(string reason)
    {
        string normalizedReason = reason.Trim();
        if (normalizedReason.Length == 0)
        {
            throw new ArgumentException("A not-applicable reason is required.", nameof(reason));
        }
        if (normalizedReason.Length > NotApplicableReasonMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }
        if (Status == SubmissionDocumentStatus.NotApplicable
            && string.Equals(NotApplicableReason, normalizedReason, StringComparison.Ordinal)
            && FileAssetId is null)
        {
            return false;
        }

        Status = SubmissionDocumentStatus.NotApplicable;
        FileAssetId = null;
        NotApplicableReason = normalizedReason;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    internal bool ApplyFinalizedFile(Guid fileAssetId, bool isAvailable)
    {
        if (fileAssetId == Guid.Empty)
        {
            throw new ArgumentException("File asset ID is required.", nameof(fileAssetId));
        }

        SubmissionDocumentStatus targetStatus = isAvailable
            ? SubmissionDocumentStatus.Available
            : SubmissionDocumentStatus.Quarantined;
        if (FileAssetId == fileAssetId && Status == targetStatus)
        {
            return false;
        }
        if (FileAssetId.HasValue)
        {
            throw Conflict("A different file asset or final status is already attached to this PPAP document.");
        }
        if (Status == SubmissionDocumentStatus.NotApplicable)
        {
            throw Conflict($"Cannot attach a finalized file to PPAP document {Id} because it is not applicable.");
        }

        FileAssetId = fileAssetId;
        NotApplicableReason = null;
        Status = targetStatus;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    internal void ApplyCurrentRevisionFile(Guid fileAssetId, bool isAvailable)
    {
        if (fileAssetId == Guid.Empty) throw new ArgumentException("File asset ID is required.", nameof(fileAssetId));
        FileAssetId = fileAssetId;
        NotApplicableReason = null;
        Status = isAvailable ? SubmissionDocumentStatus.Available : SubmissionDocumentStatus.Quarantined;
        ExtractionRequestEventId = null;
        ExtractionRequestPublishedAtUtc = null;
        ExtractionAttemptId = null;
        ExtractionTerminalStatus = null;
        CanonicalDocumentId = null;
        RawResponseFileAssetId = null;
        GeneratedExcelFileAssetId = null;
        ExtractionFailureCode = null;
        ExtractionCompletedAtUtc = null;
        ExtractionFailedAtUtc = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    internal void ClearCurrentRevisionFile()
    {
        FileAssetId = null;
        NotApplicableReason = null;
        Status = SubmissionDocumentStatus.PendingUpload;
        ExtractionRequestEventId = null;
        ExtractionRequestPublishedAtUtc = null;
        ExtractionAttemptId = null;
        ExtractionTerminalStatus = null;
        CanonicalDocumentId = null;
        RawResponseFileAssetId = null;
        GeneratedExcelFileAssetId = null;
        ExtractionFailureCode = null;
        ExtractionCompletedAtUtc = null;
        ExtractionFailedAtUtc = null;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public bool MarkExtractionRequestPublished(Guid eventId, DateTime publishedAtUtc)
    {
        if (ExtractionRequestPublishedAtUtc is not null)
        {
            return false;
        }
        if (Status != SubmissionDocumentStatus.Available || FileAssetId is null)
        {
            throw Conflict("An extraction request can only be published for an available PPAP document with a file asset.");
        }
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Event ID is required.", nameof(eventId));
        }
        if (publishedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Published time must be UTC.", nameof(publishedAtUtc));
        }

        ExtractionRequestEventId = eventId;
        ExtractionRequestPublishedAtUtc = publishedAtUtc;
        UpdatedAtUtc = publishedAtUtc;
        return true;
    }

    public bool RecordExtractionCompleted(
        string tenantId,
        string documentCode,
        Guid extractionAttemptId,
        Guid canonicalDocumentId,
        Guid? rawResponseFileAssetId,
        Guid? generatedExcelFileAssetId,
        DateTime completedAtUtc)
    {
        ValidateExtractionCorrelation(tenantId, documentCode, extractionAttemptId, completedAtUtc);
        if (canonicalDocumentId == Guid.Empty)
        {
            throw new ArgumentException("Canonical document ID is required.", nameof(canonicalDocumentId));
        }
        if (rawResponseFileAssetId == Guid.Empty)
        {
            throw new ArgumentException("Raw response file asset ID must not be empty.", nameof(rawResponseFileAssetId));
        }
        if (generatedExcelFileAssetId == Guid.Empty)
        {
            throw new ArgumentException("Generated Excel file asset ID must not be empty.", nameof(generatedExcelFileAssetId));
        }

        if (ExtractionTerminalStatus is not null)
        {
            if (ExtractionTerminalStatus == DocumentExtractionTerminalStatus.Succeeded
                && ExtractionAttemptId == extractionAttemptId
                && CanonicalDocumentId == canonicalDocumentId
                && RawResponseFileAssetId == rawResponseFileAssetId
                && GeneratedExcelFileAssetId == generatedExcelFileAssetId)
            {
                return false;
            }

            throw Conflict("A conflicting terminal extraction event was received for this PPAP document.");
        }

        ExtractionAttemptId = extractionAttemptId;
        ExtractionTerminalStatus = DocumentExtractionTerminalStatus.Succeeded;
        CanonicalDocumentId = canonicalDocumentId;
        RawResponseFileAssetId = rawResponseFileAssetId;
        GeneratedExcelFileAssetId = generatedExcelFileAssetId;
        ExtractionCompletedAtUtc = completedAtUtc;
        UpdatedAtUtc = completedAtUtc;
        return true;
    }

    public bool RecordExtractionFailed(
        string tenantId,
        string documentCode,
        Guid extractionAttemptId,
        DocumentExtractionTerminalStatus terminalStatus,
        string failureCode,
        DateTime failedAtUtc)
    {
        if (terminalStatus is not DocumentExtractionTerminalStatus.InvalidDocument
            and not DocumentExtractionTerminalStatus.Failed)
        {
            throw new ArgumentOutOfRangeException(nameof(terminalStatus));
        }
        ValidateExtractionCorrelation(tenantId, documentCode, extractionAttemptId, failedAtUtc);
        string sanitizedFailureCode = SanitizeFailureCode(failureCode);

        if (ExtractionTerminalStatus is not null)
        {
            if (ExtractionTerminalStatus == terminalStatus
                && ExtractionAttemptId == extractionAttemptId
                && string.Equals(ExtractionFailureCode, sanitizedFailureCode, StringComparison.Ordinal))
            {
                return false;
            }

            throw Conflict("A conflicting terminal extraction event was received for this PPAP document.");
        }

        ExtractionAttemptId = extractionAttemptId;
        ExtractionTerminalStatus = terminalStatus;
        ExtractionFailureCode = sanitizedFailureCode;
        ExtractionFailedAtUtc = failedAtUtc;
        UpdatedAtUtc = failedAtUtc;
        return true;
    }

    private void ValidateExtractionCorrelation(
        string tenantId,
        string documentCode,
        Guid extractionAttemptId,
        DateTime occurredOnUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentCode);
        if (!string.Equals(TenantId, tenantId.Trim(), StringComparison.Ordinal)
            || !string.Equals(DocumentCode, documentCode.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw Conflict("The extraction event does not correlate to this PPAP document.");
        }
        if (Status != SubmissionDocumentStatus.Available || ExtractionRequestPublishedAtUtc is null)
        {
            throw Conflict("Only a requested, available PPAP document can accept an extraction result.");
        }
        if (extractionAttemptId == Guid.Empty)
        {
            throw new ArgumentException("Extraction attempt ID is required.", nameof(extractionAttemptId));
        }
        if (occurredOnUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Extraction terminal time must be UTC.", nameof(occurredOnUtc));
        }
    }

    private static string SanitizeFailureCode(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        string sanitized = new(failureCode.Trim()
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .Take(ExtractionFailureCodeMaxLength)
            .ToArray());
        if (sanitized.Length == 0)
        {
            throw new ArgumentException("Failure code must contain a letter or digit.", nameof(failureCode));
        }

        return sanitized;
    }

    private static CustomException Conflict(string message) => new(
        message,
        (IEnumerable<string>?)null,
        HttpStatusCode.Conflict);
}
