namespace DES.Modules.Ppap.Contracts.v1.Credits;

public sealed record CreditBalanceDto(
    int AvailableCredits,
    int ReservedCredits);
