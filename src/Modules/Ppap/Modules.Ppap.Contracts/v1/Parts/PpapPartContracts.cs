using DES.Framework.Shared.Persistence;
using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Parts;

public sealed record PpapPartDto(
    Guid Id,
    string PartName,
    string PartNumber,
    string DrawingRevision,
    Guid? RuleTemplateId,
    Guid? RuleTemplateVersionId,
    bool IsActive,
    int Version,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record SearchMyPpapPartsQuery : IQuery<PagedResponse<PpapPartDto>>, IPagedQuery
{
    public string? Search { get; init; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}

public sealed record GetPartQuery(Guid PartId) : IQuery<PpapPartDto>;
public sealed record CreatePartCommand(string PartName, string PartNumber, string DrawingRevision) : ICommand<PpapPartDto>;
public sealed record UpdatePartCommand(Guid PartId, string PartName, string PartNumber, string DrawingRevision, int ExpectedVersion) : ICommand<PpapPartDto>;
public sealed record AssignPartTemplateCommand(Guid PartId, Guid RuleTemplateId, int ExpectedVersion) : ICommand<PpapPartDto>;
public sealed record CreateDraftFromPartCommand(Guid PartId, int PpapLevel) : ICommand<Guid>;
