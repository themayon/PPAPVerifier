using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using Mediator;

namespace DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;

public sealed class SearchMyExtractionAttemptsQuery : IPagedQuery, IQuery<PagedResponse<ExtractionAttemptDto>>
{
    public string? Status { get; set; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}
