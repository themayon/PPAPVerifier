using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed record CreateDraftSubmissionCommand(
    string PartName,
    string PartNumber,
    string DrawingRevision,
    int PpapLevel,
    Guid? RuleTemplateId = null) : ICommand<Guid>;
