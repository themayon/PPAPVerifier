using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Workspace;

public sealed record DecideFindingWaiverRequestCommand(
    Guid RequestId,
    bool Approve,
    string Reason,
    long ExpectedVersion) : ICommand;
