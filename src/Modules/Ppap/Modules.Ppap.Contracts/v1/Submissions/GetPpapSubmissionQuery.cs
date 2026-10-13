using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record GetPpapSubmissionQuery(Guid Id) : IQuery<PpapSubmissionDto>;
