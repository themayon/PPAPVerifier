using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record UpdateDraftSubmissionCommand(
    Guid Id,
    int ExpectedVersion,
    string PartName,
    string PartNumber,
    string DrawingRevision,
    int PpapLevel) : ICommand<PpapSubmissionDto>;
