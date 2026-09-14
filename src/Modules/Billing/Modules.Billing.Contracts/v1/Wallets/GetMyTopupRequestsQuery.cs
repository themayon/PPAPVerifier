using DES.Framework.Shared.Persistence;
using DES.Modules.Billing.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Billing.Contracts.v1.Wallets;

public sealed record GetMyTopupRequestsQuery(
    TopupRequestStatus? Status = null,
    int PageNumber = 1,
    int PageSize = 20) : IQuery<PagedResponse<TopupRequestDto>>;
