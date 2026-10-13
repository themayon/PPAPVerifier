using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Credits.SearchCreditTopupRequests;

public sealed class SearchCreditTopupRequestsQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<SearchCreditTopupRequestsQuery, PagedResponse<CreditTopupRequestDto>>
{
    public async ValueTask<PagedResponse<CreditTopupRequestDto>> Handle(
        SearchCreditTopupRequestsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (currentUser.GetUserId() == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        _ = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        int pageNumber = query.PageNumber ?? 1;
        int pageSize = query.PageSize ?? 20;
        IQueryable<CreditTopupRequest> requests = dbContext.CreditTopupRequests.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse(query.Status, true, out CreditTopupRequestStatus status))
        {
            requests = requests.Where(x => x.Status == status);
        }

        long totalCount = await requests.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<CreditTopupRequestDto> items = await ApplySort(requests, query.Sort)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new CreditTopupRequestDto(
                x.Id,
                x.OwnerUserId,
                x.CreditsRequested,
                x.PaymentReference,
                x.Note,
                x.Status.ToString(),
                x.RequestedAt,
                x.DecidedAt,
                x.DecidedByUserId,
                x.DecisionNote))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResponse<CreditTopupRequestDto>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static IOrderedQueryable<CreditTopupRequest> ApplySort(
        IQueryable<CreditTopupRequest> requests,
        string? sort) => sort?.ToUpperInvariant() switch
        {
            "REQUESTEDAT" => requests.OrderBy(x => x.RequestedAt).ThenBy(x => x.Id),
            "CREDITSREQUESTED" => requests.OrderBy(x => x.CreditsRequested).ThenBy(x => x.Id),
            "-CREDITSREQUESTED" => requests.OrderByDescending(x => x.CreditsRequested).ThenByDescending(x => x.Id),
            "STATUS" => requests.OrderBy(x => x.Status).ThenBy(x => x.Id),
            "-STATUS" => requests.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            "OWNERUSERID" => requests.OrderBy(x => x.OwnerUserId).ThenBy(x => x.Id),
            "-OWNERUSERID" => requests.OrderByDescending(x => x.OwnerUserId).ThenByDescending(x => x.Id),
            _ => requests.OrderByDescending(x => x.RequestedAt).ThenByDescending(x => x.Id)
        };
}
