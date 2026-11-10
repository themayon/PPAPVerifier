using System.Net;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapDocumentRevision : BaseEntity<Guid>, IHasTenant
{
    public const int FileNameMaxLength = 512;
    public const int ContentTypeMaxLength = 200;
    public const int FailureCodeMaxLength = 100;
    public const int ProviderMaxLength = 100;
    public const int ExtractorVersionMaxLength = 100;

    public string TenantId { get; private set; } = default!;
    public Guid SubmissionDocumentId { get; private set; }
    public int RevisionNumber { get; private set; }
    public long Version { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid? SourceFileAssetId { get; private set; }
    public string SourceFileName { get; private set; } = default!;
    public string SourceContentType { get; private set; } = default!;
    public long SourceSizeBytes { get; private set; }
    public bool IsCurrent { get; private set; }
    public DocumentUploadStatus UploadStatus { get; private set; }
    public DocumentProcessingStatus ProcessingStatus { get; private set; }
    public Guid? ExtractionRequestEventId { get; private set; }
    public DateTime? ExtractionRequestedAtUtc { get; private set; }
    public Guid? ExtractionAttemptId { get; private set; }
    public Guid? CanonicalDocumentId { get; private set; }
    public Guid? RawResponseFileAssetId { get; private set; }
    public Guid? GeneratedWorkbookFileAssetId { get; private set; }
    public Guid? PreviewPdfFileAssetId { get; private set; }
    public string? Provider { get; private set; }
    public string? ExtractorVersion { get; private set; }
    public string? FailureCode { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private PpapDocumentRevision() { }

    public static PpapDocumentRevision Create(
        string tenantId,
        Guid submissionDocumentId,
        int revisionNumber,
        Guid ownerUserId,
        string fileName,
        string contentType,
        long sizeBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ValidateId(submissionDocumentId, nameof(submissionDocumentId));
        ValidateId(ownerUserId, nameof(ownerUserId));
        ArgumentOutOfRangeException.ThrowIfLessThan(revisionNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(sizeBytes, 1);

        return new PpapDocumentRevision
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId.Trim(),
            SubmissionDocumentId = submissionDocumentId,
            RevisionNumber = revisionNumber,
            OwnerUserId = ownerUserId,
            SourceFileName = Normalize(fileName, FileNameMaxLength, nameof(fileName)),
            SourceContentType = Normalize(contentType, ContentTypeMaxLength, nameof(contentType)),
            SourceSizeBytes = sizeBytes,
            IsCurrent = true,
            UploadStatus = DocumentUploadStatus.Uploading,
            ProcessingStatus = DocumentProcessingStatus.NotQueued,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void ApplyFinalizedFile(Guid fileAssetId, string contentType, long sizeBytes, bool available, DateTime occurredOnUtc)
    {
        ValidateId(fileAssetId, nameof(fileAssetId));
        ValidateUtc(occurredOnUtc, nameof(occurredOnUtc));
        if (!string.Equals(SourceContentType, contentType, StringComparison.OrdinalIgnoreCase) || SourceSizeBytes != sizeBytes)
        {
            throw Conflict("The finalized file metadata does not match the reserved document revision.");
        }
        if (!IsCurrent) throw Conflict("A superseded document revision cannot accept a finalized file.");
        if (SourceFileAssetId is not null)
        {
            if (SourceFileAssetId == fileAssetId && UploadStatus == (available ? DocumentUploadStatus.Available : DocumentUploadStatus.Quarantined)) return;
            throw Conflict("A different file is already attached to this document revision.");
        }

        SourceFileAssetId = fileAssetId;
        UploadStatus = available ? DocumentUploadStatus.Available : DocumentUploadStatus.Quarantined;
        ProcessingStatus = available ? DocumentProcessingStatus.NotQueued : DocumentProcessingStatus.Failed;
        FailureCode = available ? null : "FILE_QUARANTINED";
        UpdatedAtUtc = occurredOnUtc;
        Version++;
    }

    public bool QueueExtraction(Guid eventId, DateTime occurredOnUtc)
    {
        ValidateId(eventId, nameof(eventId));
        ValidateUtc(occurredOnUtc, nameof(occurredOnUtc));
        if (ExtractionRequestEventId is not null) return ExtractionRequestEventId == eventId;
        if (!IsCurrent || UploadStatus != DocumentUploadStatus.Available || SourceFileAssetId is null)
        {
            throw Conflict("Only a current available document revision can be queued for extraction.");
        }

        ExtractionRequestEventId = eventId;
        ExtractionRequestedAtUtc = occurredOnUtc;
        ProcessingStatus = DocumentProcessingStatus.Queued;
        UpdatedAtUtc = occurredOnUtc;
        Version++;
        return true;
    }

    public void MarkExtracting(DateTime occurredOnUtc)
    {
        ValidateUtc(occurredOnUtc, nameof(occurredOnUtc));
        if (ProcessingStatus == DocumentProcessingStatus.Extracting) return;
        if (ProcessingStatus != DocumentProcessingStatus.Queued) throw Conflict("Only a queued document revision can start extraction.");
        ProcessingStatus = DocumentProcessingStatus.Extracting;
        UpdatedAtUtc = occurredOnUtc;
        Version++;
    }

    public void SetPreviewPdf(Guid previewPdfFileAssetId, DateTime occurredOnUtc)
    {
        ValidateId(previewPdfFileAssetId, nameof(previewPdfFileAssetId));
        ValidateUtc(occurredOnUtc, nameof(occurredOnUtc));
        if (PreviewPdfFileAssetId == previewPdfFileAssetId) return;
        PreviewPdfFileAssetId = previewPdfFileAssetId;
        UpdatedAtUtc = occurredOnUtc;
        Version++;
    }

    public bool RecordExtractionCompleted(
        Guid extractionAttemptId,
        Guid canonicalDocumentId,
        Guid? rawResponseFileAssetId,
        Guid? generatedWorkbookFileAssetId,
        string provider,
        string extractorVersion,
        DateTime occurredOnUtc)
    {
        ValidateId(extractionAttemptId, nameof(extractionAttemptId));
        ValidateId(canonicalDocumentId, nameof(canonicalDocumentId));
        ValidateOptionalId(rawResponseFileAssetId, nameof(rawResponseFileAssetId));
        ValidateOptionalId(generatedWorkbookFileAssetId, nameof(generatedWorkbookFileAssetId));
        ValidateUtc(occurredOnUtc, nameof(occurredOnUtc));
        if (ProcessingStatus == DocumentProcessingStatus.Ready
            && ExtractionAttemptId == extractionAttemptId
            && CanonicalDocumentId == canonicalDocumentId) return false;
        if (ProcessingStatus is DocumentProcessingStatus.Ready or DocumentProcessingStatus.Failed or DocumentProcessingStatus.Superseded)
        {
            throw Conflict("The document revision already has a conflicting terminal processing result.");
        }

        ExtractionAttemptId = extractionAttemptId;
        CanonicalDocumentId = canonicalDocumentId;
        RawResponseFileAssetId = rawResponseFileAssetId;
        GeneratedWorkbookFileAssetId = generatedWorkbookFileAssetId;
        Provider = Normalize(provider, ProviderMaxLength, nameof(provider));
        ExtractorVersion = Normalize(extractorVersion, ExtractorVersionMaxLength, nameof(extractorVersion));
        ProcessingStatus = DocumentProcessingStatus.Ready;
        FailureCode = null;
        CompletedAtUtc = occurredOnUtc;
        UpdatedAtUtc = occurredOnUtc;
        Version++;
        return true;
    }

    public bool RecordExtractionFailed(Guid extractionAttemptId, string failureCode, DateTime occurredOnUtc)
    {
        ValidateId(extractionAttemptId, nameof(extractionAttemptId));
        ValidateUtc(occurredOnUtc, nameof(occurredOnUtc));
        string normalizedCode = Normalize(failureCode, FailureCodeMaxLength, nameof(failureCode));
        if (ProcessingStatus == DocumentProcessingStatus.Failed
            && ExtractionAttemptId == extractionAttemptId
            && string.Equals(FailureCode, normalizedCode, StringComparison.Ordinal)) return false;
        if (ProcessingStatus is DocumentProcessingStatus.Ready or DocumentProcessingStatus.Failed or DocumentProcessingStatus.Superseded)
        {
            throw Conflict("The document revision already has a conflicting terminal processing result.");
        }

        ExtractionAttemptId = extractionAttemptId;
        ProcessingStatus = DocumentProcessingStatus.Failed;
        FailureCode = normalizedCode;
        CompletedAtUtc = occurredOnUtc;
        UpdatedAtUtc = occurredOnUtc;
        Version++;
        return true;
    }

    public void Supersede(DateTime occurredOnUtc)
    {
        ValidateUtc(occurredOnUtc, nameof(occurredOnUtc));
        if (!IsCurrent) return;
        if (ProcessingStatus is DocumentProcessingStatus.Queued or DocumentProcessingStatus.Converting or DocumentProcessingStatus.Extracting or DocumentProcessingStatus.Validating)
        {
            throw Conflict("Wait for the current document revision to finish processing before replacing it.");
        }

        IsCurrent = false;
        ProcessingStatus = DocumentProcessingStatus.Superseded;
        UpdatedAtUtc = occurredOnUtc;
        Version++;
    }

    private static void ValidateId(Guid id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID must not be empty.", parameterName);
    }

    private static void ValidateOptionalId(Guid? id, string parameterName)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID must not be empty when supplied.", parameterName);
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
