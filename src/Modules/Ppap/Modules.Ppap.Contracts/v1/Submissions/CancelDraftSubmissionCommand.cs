using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record CancelDraftSubmissionCommand(Guid Id, int ExpectedVersion) : ICommand<PpapSubmissionDto>;
