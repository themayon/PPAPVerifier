using System.Net;
using DES.Framework.Core.Domain;
using DES.Framework.Core.Exceptions;

namespace DES.Modules.Ppap.Domain;

public sealed class CreditTopupRequest : AggregateRoot<Guid>, IHasTenant
{
    public const int MaxPaymentReferenceLength = 200;
    public const int MaxNoteLength = 512;
    public const int MaxDecisionNoteLength = 512;
    private const int MaxTenantIdLength = 64;

    public string TenantId { get; private set; } = default!;
    public Guid OwnerUserId { get; private set; }
    public int CreditsRequested { get; private set; }
    public string? PaymentReference { get; private set; }
    public string? Note { get; private set; }
    public CreditTopupRequestStatus Status { get; private set; }
    public DateTime RequestedAt { get; private set; }
    public DateTime? DecidedAt { get; private set; }
    public Guid? DecidedByUserId { get; private set; }
    public string? DecisionNote { get; private set; }

    private CreditTopupRequest() { }

    public static CreditTopupRequest Create(
        string tenantId,
        Guid ownerUserId,
        int credits,
        string? paymentReference,
        string? note)
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
        ValidateCredits(credits);

        return new CreditTopupRequest
        {
            Id = Guid.CreateVersion7(),
            TenantId = normalizedTenantId,
            OwnerUserId = ownerUserId,
            CreditsRequested = credits,
            PaymentReference = NormalizeOptional(paymentReference, MaxPaymentReferenceLength, nameof(paymentReference)),
            Note = NormalizeOptional(note, MaxNoteLength, nameof(note)),
            Status = CreditTopupRequestStatus.Pending,
            RequestedAt = DateTime.UtcNow
        };
    }

    public void Approve(Guid decisionByUserId, string? decisionNote) =>
        Decide(CreditTopupRequestStatus.Approved, decisionByUserId, decisionNote);

    public void Reject(Guid decisionByUserId, string? decisionNote) =>
        Decide(CreditTopupRequestStatus.Rejected, decisionByUserId, decisionNote);

    private void Decide(CreditTopupRequestStatus decision, Guid decisionByUserId, string? decisionNote)
    {
        if (decisionByUserId == Guid.Empty)
        {
            throw new ArgumentException("Decision user ID is required.", nameof(decisionByUserId));
        }
        string? normalizedDecisionNote = NormalizeOptional(
            decisionNote,
            MaxDecisionNoteLength,
            nameof(decisionNote));
        if (Status != CreditTopupRequestStatus.Pending)
        {
            if (Status == decision &&
                DecidedByUserId == decisionByUserId &&
                DecisionNote == normalizedDecisionNote)
            {
                return;
            }

            throw Conflict($"Credit top-up request already has status {Status}.");
        }

        Status = decision;
        DecidedByUserId = decisionByUserId;
        DecisionNote = normalizedDecisionNote;
        DecidedAt = DateTime.UtcNow;
    }

    private static void ValidateCredits(int credits)
    {
        if (credits is not (10 or 50 or 100))
        {
            throw new ArgumentOutOfRangeException(nameof(credits), "Credits must be a fixed package of 10, 50, or 100.");
        }
    }

    private static string? NormalizeOptional(string? value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }
        string normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }

        return normalized;
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
