using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record DecidePpapSubmissionCommand(Guid SubmissionId, string Decision, string? Comment, int ExpectedVersion) : ICommand<PpapSubmissionDecisionDto>;

public sealed record PpapSubmissionDecisionDto(
    Guid SubmissionId,
    string Status,
    string Decision,
    string? Comment,
    Guid? DecidedByUserId,
    DateTime? DecidedAtUtc,
    int Version);
