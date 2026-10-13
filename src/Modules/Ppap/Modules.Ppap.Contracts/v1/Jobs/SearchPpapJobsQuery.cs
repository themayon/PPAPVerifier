using DES.Framework.Shared.Persistence;
using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Jobs;

public sealed class SearchPpapJobsQuery : IPagedQuery, IQuery<PagedResponse<PpapAdminJobDto>>
{
    public string? Status { get; set; }
    public string? Search { get; set; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}
