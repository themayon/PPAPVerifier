using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Workspace;

public sealed record GetSubmissionWorkspaceQuery(Guid SubmissionId) : IQuery<SubmissionWorkspaceDto>;
