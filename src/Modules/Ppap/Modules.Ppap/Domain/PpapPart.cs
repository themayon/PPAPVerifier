using System.Net;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapPart : AggregateRoot<Guid>, IHasTenant
{
    public string TenantId { get; private set; } = default!;
    public Guid OwnerUserId { get; private set; }
    public string PartName { get; private set; } = default!;
    public string PartNumber { get; private set; } = default!;
    public string DrawingRevision { get; private set; } = default!;
    public Guid? RuleTemplateId { get; private set; }
    public Guid? RuleTemplateVersionId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public int Version { get; private set; }

    private PpapPart() { }

    public static PpapPart Create(
        string tenantId,
        Guid ownerUserId,
        string partName,
        string partNumber,
        string drawingRevision)
    {
        ValidateIdentity(tenantId, ownerUserId);
        return new PpapPart
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId.Trim(),
            OwnerUserId = ownerUserId,
            PartName = NormalizeRequired(partName, 200, nameof(partName)),
            PartNumber = NormalizeRequired(partNumber, 100, nameof(partNumber)),
            DrawingRevision = NormalizeRequired(drawingRevision, 50, nameof(drawingRevision)),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            Version = 1
        };
    }

    public void UpdateDetails(string partName, string partNumber, string drawingRevision, int expectedVersion)
    {
        GuardVersion(expectedVersion);
        PartName = NormalizeRequired(partName, 200, nameof(partName));
        PartNumber = NormalizeRequired(partNumber, 100, nameof(partNumber));
        DrawingRevision = NormalizeRequired(drawingRevision, 50, nameof(drawingRevision));
        Touch();
    }

    public void AssignTemplate(Guid ruleTemplateId, Guid ruleTemplateVersionId, int expectedVersion)
    {
        GuardVersion(expectedVersion);
        if (ruleTemplateId == Guid.Empty) throw new ArgumentException("Rule template is required.", nameof(ruleTemplateId));
        if (ruleTemplateVersionId == Guid.Empty) throw new ArgumentException("Rule template version is required.", nameof(ruleTemplateVersionId));
        if (RuleTemplateId == ruleTemplateId && RuleTemplateVersionId == ruleTemplateVersionId) return;
        RuleTemplateId = ruleTemplateId;
        RuleTemplateVersionId = ruleTemplateVersionId;
        Touch();
    }

    public void ClearTemplate(int expectedVersion)
    {
        GuardVersion(expectedVersion);
        if (RuleTemplateId is null && RuleTemplateVersionId is null) return;
        RuleTemplateId = null;
        RuleTemplateVersionId = null;
        Touch();
    }

    private void GuardVersion(int expectedVersion)
    {
        if (Version != expectedVersion)
        {
            throw new CustomException(
                $"PPAP part version conflict. Expected version {expectedVersion}, but current version is {Version}.",
                (IEnumerable<string>?)null,
                HttpStatusCode.Conflict);
        }
    }

    private void Touch()
    {
        Version = checked(Version + 1);
        UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void ValidateIdentity(string tenantId, Guid ownerUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner is required.", nameof(ownerUserId));
    }

    private static string NormalizeRequired(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
        string normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(parameterName, $"Value cannot exceed {maxLength} characters.");
        return normalized;
    }
}
