using System.Net;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapSubmission : AggregateRoot<Guid>, IHasTenant
{
    private readonly List<PpapSubmissionDocument> _documents = [];

    public string TenantId { get; private set; } = default!;
    public Guid OwnerUserId { get; private set; }
    public Guid? PartId { get; private set; }
    public string PartName { get; private set; } = default!;
    public string PartNumber { get; private set; } = default!;
    public string DrawingRevision { get; private set; } = default!;
    public int PpapLevel { get; private set; }
    public Guid? RuleTemplateId { get; private set; }
    public Guid? RuleTemplateVersionId { get; private set; }
    public SubmissionStatus Status { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public DateTime? SubmittedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public IReadOnlyCollection<PpapSubmissionDocument> Documents => _documents.AsReadOnly();

    private PpapSubmission() { }

    public static PpapSubmission CreateDraft(
        string tenantId,
        Guid ownerUserId,
        string partName,
        string partNumber,
        string drawingRevision,
        int ppapLevel,
        Guid? ruleTemplateId = null,
        Guid? ruleTemplateVersionId = null,
        Guid? partId = null)
    {
        Validate(tenantId, ownerUserId, partName, partNumber, drawingRevision, ppapLevel);
        ValidateTemplateSnapshot(ruleTemplateId, ruleTemplateVersionId);
        if (partId == Guid.Empty) throw new ArgumentException("Part ID must not be empty.", nameof(partId));
        return new PpapSubmission
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId.Trim(),
            OwnerUserId = ownerUserId,
            PartId = partId,
            PartName = partName.Trim(),
            PartNumber = partNumber.Trim(),
            DrawingRevision = drawingRevision.Trim(),
            PpapLevel = ppapLevel,
            RuleTemplateId = ruleTemplateId,
            RuleTemplateVersionId = ruleTemplateVersionId,
            Status = SubmissionStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public PpapSubmissionDocument AddDocumentSlot(string code, SubmissionDocumentDisposition disposition = SubmissionDocumentDisposition.Submit)
    {
        EnsureDraft("add document slots to");
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        string normalizedCode = code.Trim().ToUpperInvariant();
        if (!PpapDocumentCodes.All.Contains(normalizedCode))
        {
            throw new ArgumentException($"Unsupported PPAP document code '{normalizedCode}'.", nameof(code));
        }

        PpapSubmissionDocument? existing = _documents.FirstOrDefault(
            x => string.Equals(x.DocumentCode, normalizedCode, StringComparison.Ordinal));
        if (existing is not null)
        {
            if (existing.Disposition != disposition)
            {
                throw Conflict($"PPAP document '{normalizedCode}' already exists with a different disposition.");
            }

            return existing;
        }

        PpapSubmissionDocument document = PpapSubmissionDocument.Create(TenantId, Id, normalizedCode, disposition);
        _documents.Add(document);
        RecordMutation();
        return document;
    }

    public void MarkDocumentNotApplicable(Guid documentId, string reason)
    {
        EnsureDraft("mark document slots not applicable on");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        PpapSubmissionDocument document = GetDocument(documentId);
        if (document.MarkNotApplicable(reason))
        {
            RecordMutation();
        }
    }

    public void ApplyFinalizedFile(Guid documentId, Guid fileAssetId, bool isAvailable)
    {
        EnsureDraft("attach finalized files to");
        PpapSubmissionDocument document = GetDocument(documentId);
        if (document.ApplyFinalizedFile(fileAssetId, isAvailable))
        {
            RecordMutation();
        }
    }

    public void UpdateDraft(string partName, string partNumber, string drawingRevision, int ppapLevel)
    {
        EnsureDraft("update");
        Validate(TenantId, OwnerUserId, partName, partNumber, drawingRevision, ppapLevel);
        PartName = partName.Trim();
        PartNumber = partNumber.Trim();
        DrawingRevision = drawingRevision.Trim();
        PpapLevel = ppapLevel;
        Version = checked(Version + 1);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Submit()
    {
        EnsureDraft("submit");
        if (!_documents.Any(x => x.Status == SubmissionDocumentStatus.Available))
        {
            throw Conflict("A PPAP submission requires at least one available document.");
        }
        if (_documents.Any(x => x.Status == SubmissionDocumentStatus.Quarantined
            || (x.Status == SubmissionDocumentStatus.PendingUpload && x.Disposition != SubmissionDocumentDisposition.Retain)))
        {
            throw Conflict("A PPAP submission cannot contain quarantined documents or pending Submit/Available documents.");
        }

        Status = SubmissionStatus.Submitted;
        Version = checked(Version + 1);
        SubmittedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = SubmittedAtUtc;
    }

    public void Cancel()
    {
        EnsureDraft("cancel");
        Status = SubmissionStatus.Cancelled;
        Version = checked(Version + 1);
        CancelledAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CancelledAtUtc;
    }

    private PpapSubmissionDocument GetDocument(Guid documentId)
    {
        if (documentId == Guid.Empty)
        {
            throw new ArgumentException("Document ID is required.", nameof(documentId));
        }

        return _documents.FirstOrDefault(x => x.Id == documentId)
            ?? throw new KeyNotFoundException($"PPAP document {documentId} was not found on submission {Id}.");
    }

    private void RecordMutation()
    {
        Version = checked(Version + 1);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private void EnsureDraft(string action)
    {
        if (Status != SubmissionStatus.Draft)
        {
            throw new CustomException(
                $"Cannot {action} a PPAP submission in status {Status}.",
                (IEnumerable<string>?)null,
                HttpStatusCode.Conflict);
        }
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);

    private static void ValidateTemplateSnapshot(Guid? ruleTemplateId, Guid? ruleTemplateVersionId)
    {
        if (ruleTemplateId.HasValue != ruleTemplateVersionId.HasValue)
        {
            throw new ArgumentException("Rule template and version IDs must be supplied together.");
        }
        if (ruleTemplateId == Guid.Empty)
        {
            throw new ArgumentException("Rule template ID is required when a template snapshot is supplied.", nameof(ruleTemplateId));
        }
        if (ruleTemplateVersionId == Guid.Empty)
        {
            throw new ArgumentException("Rule template version ID is required when a template snapshot is supplied.", nameof(ruleTemplateVersionId));
        }
    }

    private static void Validate(
        string tenantId,
        Guid ownerUserId,
        string partName,
        string partNumber,
        string drawingRevision,
        int ppapLevel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partName);
        ArgumentException.ThrowIfNullOrWhiteSpace(partNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(drawingRevision);
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner user ID is required.", nameof(ownerUserId));
        if (ppapLevel is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(ppapLevel));
        if (partName.Trim().Length > 200) throw new ArgumentOutOfRangeException(nameof(partName));
        if (partNumber.Trim().Length > 100) throw new ArgumentOutOfRangeException(nameof(partNumber));
        if (drawingRevision.Trim().Length > 50) throw new ArgumentOutOfRangeException(nameof(drawingRevision));
    }
}
