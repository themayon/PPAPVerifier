using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Workspace;

public sealed record RequestFindingWaiverCommand(
    Guid SubmissionId,
    Guid WorkingFindingId,
    string Reason) : ICommand<Guid>;
