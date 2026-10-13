using System.Text.Json;
using DES.Framework.Core.Domain;

namespace DES.Modules.DocumentIntelligence.Domain;

public sealed class CanonicalDocument : AggregateRoot<Guid>, IHasTenant
{
    public const int TenantIdMaxLength = 64;
    public const int SchemaVersionMaxLength = 32;

    public string TenantId { get; private set; } = default!;
    public Guid ExtractionAttemptId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string SchemaVersion { get; private set; } = default!;
    public string CanonicalJson { get; private set; } = default!;
    public Guid? RawResponseFileAssetId { get; private set; }
    public Guid? GeneratedExcelFileAssetId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private CanonicalDocument() { }

    public static CanonicalDocument Create(
        string tenantId,
        Guid extractionAttemptId,
        Guid ownerUserId,
        string schemaVersion,
        string canonicalJson,
        Guid? rawResponseFileAssetId = null,
        Guid? generatedExcelFileAssetId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        string normalizedTenantId = tenantId.Trim();
        if (normalizedTenantId.Length > TenantIdMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        }
        EnsureId(extractionAttemptId, nameof(extractionAttemptId));
        EnsureId(ownerUserId, nameof(ownerUserId));
        ValidateOptionalId(rawResponseFileAssetId, nameof(rawResponseFileAssetId));
        ValidateOptionalId(generatedExcelFileAssetId, nameof(generatedExcelFileAssetId));
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        string normalizedSchemaVersion = schemaVersion.Trim();
        if (normalizedSchemaVersion.Length > SchemaVersionMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalJson);
        using JsonDocument _ = JsonDocument.Parse(canonicalJson);

        return new CanonicalDocument
        {
            Id = Guid.CreateVersion7(),
            TenantId = normalizedTenantId,
            ExtractionAttemptId = extractionAttemptId,
            OwnerUserId = ownerUserId,
            SchemaVersion = normalizedSchemaVersion,
            CanonicalJson = canonicalJson,
            RawResponseFileAssetId = rawResponseFileAssetId,
            GeneratedExcelFileAssetId = generatedExcelFileAssetId,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static void EnsureId(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required.", parameterName);
        }
    }

    private static void ValidateOptionalId(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identifier is required when supplied.", parameterName);
        }
    }
}
