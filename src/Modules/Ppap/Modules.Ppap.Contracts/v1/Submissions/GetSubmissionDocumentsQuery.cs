using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record ListSubmissionDocumentsQuery(Guid SubmissionId)
    : IQuery<IReadOnlyList<SubmissionDocumentDto>>;
