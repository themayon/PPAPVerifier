using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Credits;

public sealed record CreateCreditTopupRequestCommand(
    int CreditsRequested,
    string? PaymentReference,
    string? Note) : ICommand<Guid>;
