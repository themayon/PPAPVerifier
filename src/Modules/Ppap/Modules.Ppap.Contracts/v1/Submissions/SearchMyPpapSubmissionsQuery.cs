using DES.Framework.Shared.Persistence;
using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Submissions;

public sealed class SearchMyPpapSubmissionsQuery : IPagedQuery, IQuery<PagedResponse<PpapSubmissionDto>>
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}
