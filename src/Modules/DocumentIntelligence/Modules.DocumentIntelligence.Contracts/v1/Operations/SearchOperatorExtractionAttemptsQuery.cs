using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using Mediator;

namespace DES.Modules.DocumentIntelligence.Contracts.v1.Operations;

public sealed class SearchOperatorExtractionAttemptsQuery : IPagedQuery, IQuery<PagedResponse<OperatorExtractionAttemptDto>>
{
    public string? Status { get; set; }
    public string? DocumentCode { get; set; }
    public Guid? OwnerUserId { get; set; }
    public int? PageNumber { get; set; } = 1;
    public int? PageSize { get; set; } = 20;
    public string? Sort { get; set; }
}
