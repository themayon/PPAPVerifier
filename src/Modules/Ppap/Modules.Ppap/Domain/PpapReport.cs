using System.Text;
using System.Text.Json;
using DES.Framework.Core.Domain;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapReport : AggregateRoot<Guid>, IHasTenant
{
    public const int ReproducibilityManifestMaxBytes = 256 * 1024;
    public const string ReproducibilityManifestSchemaVersion = "1.0";

    public string TenantId { get; private set; } = default!;
    public Guid SubmissionId { get; private set; }
    public Guid JobId { get; private set; }
    public int SubmissionVersion { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public DateTime GeneratedAtUtc { get; private set; }
    public int AvailableDocumentCount { get; private set; }
    public int NotApplicableDocumentCount { get; private set; }
    public int QuarantinedDocumentCount { get; private set; }
    public int FindingCount { get; private set; }
    public int FailedFindingCount { get; private set; }
    public int NotEvaluatedFindingCount { get; private set; }
    public int ErrorFindingCount { get; private set; }
    public string? ReproducibilityManifestJson { get; private set; }

    private PpapReport() { }

    public static PpapReport Create(
        string tenantId,
        Guid submissionId,
        Guid jobId,
        int submissionVersion,
        Guid ownerUserId,
        int availableDocumentCount,
        int notApplicableDocumentCount,
        int quarantinedDocumentCount,
        int findingCount,
        int failedFindingCount,
        int notEvaluatedFindingCount,
        int errorFindingCount,
        string reproducibilityManifestJson)
    {
        ValidateManifest(reproducibilityManifestJson);
        return CreateCore(
            tenantId,
            submissionId,
            jobId,
            submissionVersion,
            ownerUserId,
            availableDocumentCount,
            notApplicableDocumentCount,
            quarantinedDocumentCount,
            findingCount,
            failedFindingCount,
            notEvaluatedFindingCount,
            errorFindingCount,
            reproducibilityManifestJson);
    }

    internal static PpapReport CreateFixture(
        string tenantId,
        Guid submissionId,
        Guid jobId,
        int submissionVersion,
        Guid ownerUserId,
        int availableDocumentCount,
        int notApplicableDocumentCount,
        int quarantinedDocumentCount,
        int findingCount,
        int failedFindingCount,
        int notEvaluatedFindingCount,
        int errorFindingCount) => CreateCore(
            tenantId,
            submissionId,
            jobId,
            submissionVersion,
            ownerUserId,
            availableDocumentCount,
            notApplicableDocumentCount,
            quarantinedDocumentCount,
            findingCount,
            failedFindingCount,
            notEvaluatedFindingCount,
            errorFindingCount,
            null);

    private static PpapReport CreateCore(
        string tenantId,
        Guid submissionId,
        Guid jobId,
        int submissionVersion,
        Guid ownerUserId,
        int availableDocumentCount,
        int notApplicableDocumentCount,
        int quarantinedDocumentCount,
        int findingCount,
        int failedFindingCount,
        int notEvaluatedFindingCount,
        int errorFindingCount,
        string? reproducibilityManifestJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        string normalizedTenantId = tenantId.Trim();
        if (normalizedTenantId.Length > 64) throw new ArgumentOutOfRangeException(nameof(tenantId));
        if (submissionId == Guid.Empty) throw new ArgumentException("Submission ID is required.", nameof(submissionId));
        if (jobId == Guid.Empty) throw new ArgumentException("Job ID is required.", nameof(jobId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(submissionVersion);
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner user ID is required.", nameof(ownerUserId));
        ArgumentOutOfRangeException.ThrowIfNegative(availableDocumentCount);
        ArgumentOutOfRangeException.ThrowIfNegative(notApplicableDocumentCount);
        ArgumentOutOfRangeException.ThrowIfNegative(quarantinedDocumentCount);
        ArgumentOutOfRangeException.ThrowIfNegative(findingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(failedFindingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(notEvaluatedFindingCount);
        ArgumentOutOfRangeException.ThrowIfNegative(errorFindingCount);
        if (failedFindingCount + notEvaluatedFindingCount + errorFindingCount != findingCount)
        {
            throw new ArgumentException("Finding outcome counts must equal the total finding count.", nameof(findingCount));
        }

        return new PpapReport
        {
            Id = Guid.CreateVersion7(),
            TenantId = normalizedTenantId,
            SubmissionId = submissionId,
            JobId = jobId,
            SubmissionVersion = submissionVersion,
            OwnerUserId = ownerUserId,
            GeneratedAtUtc = DateTime.UtcNow,
            AvailableDocumentCount = availableDocumentCount,
            NotApplicableDocumentCount = notApplicableDocumentCount,
            QuarantinedDocumentCount = quarantinedDocumentCount,
            FindingCount = findingCount,
            FailedFindingCount = failedFindingCount,
            NotEvaluatedFindingCount = notEvaluatedFindingCount,
            ErrorFindingCount = errorFindingCount,
            ReproducibilityManifestJson = reproducibilityManifestJson
        };
    }

    private static void ValidateManifest(string manifestJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        if (Encoding.UTF8.GetByteCount(manifestJson) > ReproducibilityManifestMaxBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(manifestJson));
        }

        using JsonDocument document = JsonDocument.Parse(manifestJson, new JsonDocumentOptions { MaxDepth = 16 });
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("schemaVersion", out JsonElement schemaVersion)
            || schemaVersion.ValueKind != JsonValueKind.String
            || !string.Equals(schemaVersion.GetString(), ReproducibilityManifestSchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("The reproducibility manifest must be a schema version 1.0 JSON object.", nameof(manifestJson));
        }
    }
}
