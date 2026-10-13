using DES.Framework.Shared.Persistence;
using Mediator;

namespace DES.Modules.Ppap.Contracts.v1.Credits;

public sealed class SearchMyCreditTopupRequestsQuery : IPagedQuery, IQuery<PagedResponse<CreditTopupRequestDto>>
{
    public string? Status { get; set; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}
