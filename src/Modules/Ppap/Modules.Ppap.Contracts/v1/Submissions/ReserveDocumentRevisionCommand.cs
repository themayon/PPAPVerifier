using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record ReserveDocumentRevisionCommand(
    Guid SubmissionId,
    Guid DocumentId,
    int ExpectedVersion,
    string FileName,
    string ContentType,
    long SizeBytes) : ICommand<Guid>;
