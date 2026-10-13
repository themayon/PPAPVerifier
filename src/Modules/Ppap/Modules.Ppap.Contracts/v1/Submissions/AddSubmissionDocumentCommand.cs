using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record AddSubmissionDocumentCommand(Guid SubmissionId, string DocumentCode) : ICommand<Guid>;
