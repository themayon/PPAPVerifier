using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Credits.SearchMyCreditTopupRequests;

public sealed class SearchMyCreditTopupRequestsQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<SearchMyCreditTopupRequestsQuery, PagedResponse<CreditTopupRequestDto>>
{
    public async ValueTask<PagedResponse<CreditTopupRequestDto>> Handle(
        SearchMyCreditTopupRequestsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        _ = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        int pageNumber = query.PageNumber ?? 1;
        int pageSize = query.PageSize ?? 20;
        IQueryable<Domain.CreditTopupRequest> requests = dbContext.CreditTopupRequests
            .AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId);

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse(query.Status, true, out Domain.CreditTopupRequestStatus status))
        {
            requests = requests.Where(x => x.Status == status);
        }

        long totalCount = await requests.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<CreditTopupRequestDto> items = await requests
            .OrderByDescending(x => x.RequestedAt)
            .ThenByDescending(x => x.Id)
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
}
