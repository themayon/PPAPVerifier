using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record MarkSubmissionDocumentNotApplicableCommand(
    Guid SubmissionId,
    Guid DocumentId,
    string Reason) : ICommand;
