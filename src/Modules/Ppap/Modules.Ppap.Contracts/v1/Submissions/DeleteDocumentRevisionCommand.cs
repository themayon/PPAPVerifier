using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record DeleteDocumentRevisionCommand(
    Guid SubmissionId,
    Guid DocumentId,
    Guid RevisionId,
    int ExpectedVersion) : ICommand<Unit>;
