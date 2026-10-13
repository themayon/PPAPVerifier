namespace DES.Modules.Ppap.Contracts.v1.Credits;

public sealed record CreditTopupRequestDto(
    Guid Id,
    Guid OwnerUserId,
    int CreditsRequested,
    string? PaymentReference,
    string? Note,
    string Status,
    DateTime RequestedAt,
    DateTime? DecidedAt,
    Guid? DecidedByUserId,
    string? DecisionNote);
