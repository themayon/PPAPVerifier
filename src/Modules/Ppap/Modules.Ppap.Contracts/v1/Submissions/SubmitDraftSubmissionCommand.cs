using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record SubmitDraftSubmissionCommand(Guid Id, int ExpectedVersion) : ICommand<Guid>;
