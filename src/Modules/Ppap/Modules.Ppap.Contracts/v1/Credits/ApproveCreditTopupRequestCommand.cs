using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Credits;

public sealed record ApproveCreditTopupRequestCommand(
    Guid Id,
    string? DecisionNote) : ICommand<Unit>;
