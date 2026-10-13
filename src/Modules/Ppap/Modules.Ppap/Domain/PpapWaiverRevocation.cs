using DES.Framework.Core.Domain;

namespace DES.Modules.Ppap.Domain;

public sealed class PpapWaiverRevocation : BaseEntity<Guid>, IHasTenant
{
    public const int ReasonMaxLength = 1000;

    public string TenantId { get; private set; } = default!;
    public Guid WaiverId { get; private set; }
    public Guid RevokedByUserId { get; private set; }
    public string Reason { get; private set; } = default!;
    public DateTime RevokedAtUtc { get; private set; }

    private PpapWaiverRevocation() { }

    public static PpapWaiverRevocation Create(
        string tenantId,
        Guid waiverId,
        Guid revokedByUserId,
        string reason,
        DateTime revokedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (waiverId == Guid.Empty) throw new ArgumentException("Waiver ID must not be empty.", nameof(waiverId));
        if (revokedByUserId == Guid.Empty) throw new ArgumentException("Revocation actor ID must not be empty.", nameof(revokedByUserId));
        if (revokedAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Revocation timestamp must be UTC.", nameof(revokedAtUtc));
        string normalizedReason = reason?.Trim() ?? string.Empty;
        if (normalizedReason.Length == 0) throw new ArgumentException("Revocation reason is required.", nameof(reason));
        if (normalizedReason.Length > ReasonMaxLength) throw new ArgumentOutOfRangeException(nameof(reason));
        string normalizedTenantId = tenantId.Trim();
        if (normalizedTenantId.Length > 64) throw new ArgumentOutOfRangeException(nameof(tenantId));

        return new PpapWaiverRevocation
        {
            Id = Guid.CreateVersion7(),
            TenantId = normalizedTenantId,
            WaiverId = waiverId,
            RevokedByUserId = revokedByUserId,
            Reason = normalizedReason,
            RevokedAtUtc = revokedAtUtc
        };
    }
}
