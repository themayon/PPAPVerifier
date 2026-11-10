using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Workspace;

public sealed record WaiverReviewDto(
    WaiverRequestDto Request,
    string StableRuleCode,
    string RuleName,
    string FindingMessage,
    string EvidenceJson);

public sealed record GetPendingWaiverRequestsQuery(Guid SubmissionId) : IQuery<IReadOnlyList<WaiverReviewDto>>;
