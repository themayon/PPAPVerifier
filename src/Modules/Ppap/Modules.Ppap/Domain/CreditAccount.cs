using System.Net;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;

namespace DES.Modules.Ppap.Domain;

public sealed class CreditAccount : AggregateRoot<Guid>, IHasTenant
{
    private const int MaxTenantIdLength = 64;
    private const int MaxReferenceLength = 200;
    private readonly List<CreditTransaction> _transactions = [];

    public string TenantId { get; private set; } = default!;
    public Guid OwnerUserId { get; private set; }
    public int AvailableCredits { get; private set; }
    public int ReservedCredits { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<CreditTransaction> Transactions => _transactions.AsReadOnly();

    private CreditAccount() { }

    public static CreditAccount Create(string tenantId, Guid ownerUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        string normalizedTenantId = tenantId.Trim();
        if (normalizedTenantId.Length > MaxTenantIdLength)
        {
            throw new ArgumentOutOfRangeException(nameof(tenantId));
        }
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Owner user ID is required.", nameof(ownerUserId));
        }

        return new CreditAccount
        {
            Id = Guid.CreateVersion7(),
            TenantId = normalizedTenantId,
            OwnerUserId = ownerUserId,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public CreditTransaction TopUp(int amount, string reference)
    {
        string normalizedReference = ValidateOperation(amount, reference);
        CreditTransaction? existing = FindExisting(CreditTransactionKind.Topup, normalizedReference, amount);
        if (existing is not null)
        {
            return existing;
        }

        int availableCredits = checked(AvailableCredits + amount);
        return Record(CreditTransactionKind.Topup, amount, normalizedReference, availableCredits, ReservedCredits);
    }

    public CreditTransaction Reserve(int amount, string reference)
    {
        string normalizedReference = ValidateOperation(amount, reference);
        CreditTransaction? existing = FindExisting(CreditTransactionKind.Reserve, normalizedReference, amount);
        if (existing is not null)
        {
            return existing;
        }
        if (AvailableCredits < amount)
        {
            throw Conflict("Insufficient available credits.");
        }

        return Record(
            CreditTransactionKind.Reserve,
            amount,
            normalizedReference,
            AvailableCredits - amount,
            checked(ReservedCredits + amount));
    }

    public CreditTransaction CommitReservation(int amount, string reference)
    {
        string normalizedReference = ValidateOperation(amount, reference);
        CreditTransaction? existing = FindExisting(CreditTransactionKind.Commit, normalizedReference, amount);
        if (existing is not null)
        {
            return existing;
        }
        if (ReservedCredits < amount)
        {
            throw Conflict("Insufficient reserved credits.");
        }

        return Record(
            CreditTransactionKind.Commit,
            amount,
            normalizedReference,
            AvailableCredits,
            ReservedCredits - amount);
    }

    public CreditTransaction ReleaseReservation(int amount, string reference)
    {
        string normalizedReference = ValidateOperation(amount, reference);
        CreditTransaction? existing = FindExisting(CreditTransactionKind.Release, normalizedReference, amount);
        if (existing is not null)
        {
            return existing;
        }
        if (ReservedCredits < amount)
        {
            throw Conflict("Insufficient reserved credits.");
        }

        return Record(
            CreditTransactionKind.Release,
            amount,
            normalizedReference,
            checked(AvailableCredits + amount),
            ReservedCredits - amount);
    }

    public CreditTransaction Refund(int amount, string reference)
    {
        string normalizedReference = ValidateOperation(amount, reference);
        CreditTransaction? existing = FindExisting(CreditTransactionKind.Refund, normalizedReference, amount);
        if (existing is not null)
        {
            return existing;
        }

        int availableCredits = checked(AvailableCredits + amount);
        return Record(CreditTransactionKind.Refund, amount, normalizedReference, availableCredits, ReservedCredits);
    }

    private static string ValidateOperation(int amount, string reference)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Credit amount must be positive.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        string normalizedReference = reference.Trim();
        if (normalizedReference.Length > MaxReferenceLength)
        {
            throw new ArgumentOutOfRangeException(nameof(reference));
        }

        return normalizedReference;
    }

    private CreditTransaction? FindExisting(CreditTransactionKind kind, string reference, int amount)
    {
        CreditTransaction? existing = _transactions.Find(x => x.Kind == kind && x.Reference == reference);
        if (existing is null || existing.Amount == amount)
        {
            return existing;
        }

        throw Conflict($"Credit operation reference '{reference}' was already used with different details.");
    }

    private CreditTransaction Record(
        CreditTransactionKind kind,
        int amount,
        string reference,
        int availableCredits,
        int reservedCredits)
    {
        if (availableCredits < 0 || reservedCredits < 0)
        {
            throw Conflict("Credit balances cannot be negative.");
        }

        DateTime now = DateTime.UtcNow;
        CreditTransaction transaction = CreditTransaction.Create(TenantId, Id, kind, amount, reference, now);
        AvailableCredits = availableCredits;
        ReservedCredits = reservedCredits;
        Version = checked(Version + 1);
        UpdatedAtUtc = now;
        _transactions.Add(transaction);
        return transaction;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
