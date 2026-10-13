using DES.Framework.Core.Domain;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapManualFinding : BaseEntity<Guid>, IHasTenant
{
    public const int MessageMaxLength = 1000;
    public const int EvidenceNoteMaxLength = 2000;

    public string TenantId { get; private set; } = default!;
    public Guid ReportId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public Guid JobId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public Guid? SubmissionDocumentId { get; private set; }
    public Guid? CanonicalDocumentId { get; private set; }
    public Guid? ExtractionAttemptId { get; private set; }
    public string? DocumentCode { get; private set; }
    public string Message { get; private set; } = default!;
    public string? EvidenceNote { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private PpapManualFinding() { }

    public static PpapManualFinding Create(
        string tenantId,
        Guid reportId,
        Guid submissionId,
        Guid jobId,
        Guid ownerUserId,
        string message,
        string? evidenceNote,
        Guid createdByUserId,
        DateTime createdAtUtc,
        Guid? submissionDocumentId = null,
        Guid? canonicalDocumentId = null,
        Guid? extractionAttemptId = null,
        string? documentCode = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ValidateId(reportId, nameof(reportId));
        ValidateId(submissionId, nameof(submissionId));
        ValidateId(jobId, nameof(jobId));
        ValidateId(ownerUserId, nameof(ownerUserId));
        ValidateId(createdByUserId, nameof(createdByUserId));
        if (createdAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Creation timestamp must be UTC.", nameof(createdAtUtc));
        bool hasAnyCorrelation = submissionDocumentId is not null || canonicalDocumentId is not null || extractionAttemptId is not null;
        bool hasAllCorrelation = submissionDocumentId is not null && canonicalDocumentId is not null && extractionAttemptId is not null;
        if (hasAnyCorrelation && !hasAllCorrelation) throw new ArgumentException("Document correlation must include submission document, canonical document, and extraction attempt IDs.", nameof(submissionDocumentId));
        if (submissionDocumentId == Guid.Empty || canonicalDocumentId == Guid.Empty || extractionAttemptId == Guid.Empty) throw new ArgumentException("Document correlation IDs must not be empty.", nameof(submissionDocumentId));
        string? normalizedDocumentCode = hasAllCorrelation
            ? NormalizeRequired(documentCode!, PpapSubmissionDocument.DocumentCodeMaxLength, nameof(documentCode))
            : NormalizeOptional(documentCode, PpapSubmissionDocument.DocumentCodeMaxLength, nameof(documentCode));
        if (!hasAllCorrelation && normalizedDocumentCode is not null) throw new ArgumentException("A document code requires complete document correlation.", nameof(documentCode));

        return new PpapManualFinding
        {
            Id = Guid.CreateVersion7(),
            TenantId = NormalizeRequired(tenantId, 64, nameof(tenantId)),
            ReportId = reportId,
            SubmissionId = submissionId,
            JobId = jobId,
            OwnerUserId = ownerUserId,
            SubmissionDocumentId = submissionDocumentId,
            CanonicalDocumentId = canonicalDocumentId,
            ExtractionAttemptId = extractionAttemptId,
            DocumentCode = normalizedDocumentCode?.ToUpperInvariant(),
            Message = NormalizeRequired(message, MessageMaxLength, nameof(message)),
            EvidenceNote = NormalizeOptional(evidenceNote, EvidenceNoteMaxLength, nameof(evidenceNote)),
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = createdAtUtc
        };
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

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(parameterName);
        return normalized;
    }
}
