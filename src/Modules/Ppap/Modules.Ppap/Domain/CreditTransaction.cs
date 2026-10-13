using DES.Framework.Core.Domain;

namespace DES.Modules.Ppap.Domain;

public sealed class CreditTransaction : BaseEntity<Guid>, IHasTenant
{
    public string TenantId { get; private set; } = default!;
    public Guid CreditAccountId { get; private set; }
    public CreditTransactionKind Kind { get; private set; }
    public int Amount { get; private set; }
    public string Reference { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }

    private CreditTransaction() { }

    internal static CreditTransaction Create(
        string tenantId,
        Guid creditAccountId,
        CreditTransactionKind kind,
        int amount,
        string reference,
        DateTime createdAtUtc) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            CreditAccountId = creditAccountId,
            Kind = kind,
            Amount = amount,
            Reference = reference,
            CreatedAtUtc = createdAtUtc
        };
}
