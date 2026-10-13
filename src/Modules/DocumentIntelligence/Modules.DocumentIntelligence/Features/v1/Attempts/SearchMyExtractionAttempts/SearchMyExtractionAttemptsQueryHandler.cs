using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.DocumentIntelligence.Features.v1.Attempts.SearchMyExtractionAttempts;

public sealed class SearchMyExtractionAttemptsQueryHandler(
    DocumentIntelligenceDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<SearchMyExtractionAttemptsQuery, PagedResponse<ExtractionAttemptDto>>
{
    public async ValueTask<PagedResponse<ExtractionAttemptDto>> Handle(
        SearchMyExtractionAttemptsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        _ = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");

        int pageNumber = query.PageNumber ?? 1;
        int pageSize = query.PageSize ?? 20;
        IQueryable<ExtractionAttempt> attempts = dbContext.ExtractionAttempts
            .AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId);
        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse(query.Status, true, out ExtractionAttemptStatus status))
        {
            attempts = attempts.Where(x => x.Status == status);
        }

        long totalCount = await attempts.LongCountAsync(cancellationToken).ConfigureAwait(false);
        IOrderedQueryable<ExtractionAttempt> ordered = query.Sort?.ToUpperInvariant() switch
        {
            "CREATEDAT" => attempts.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            "UPDATEDAT" => attempts.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            "-UPDATEDAT" => attempts.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id),
            "STATUS" => attempts.OrderBy(x => x.Status).ThenBy(x => x.Id),
            "-STATUS" => attempts.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            _ => attempts.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
        };
        List<ExtractionAttemptDto> items = await ordered
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new ExtractionAttemptDto(
                x.Id,
                x.SubmissionDocumentId,
                x.SourceFileAssetId,
                x.DocumentCode,
                x.SourceSha256,
                x.Provider,
                x.Model,
                x.ExtractorVersion,
                x.Status.ToString(),
                x.AttemptCount,
                x.ErrorCode,
                x.ErrorMessage,
                dbContext.CanonicalDocuments.Where(c => c.ExtractionAttemptId == x.Id).Select(c => (Guid?)c.Id).FirstOrDefault(),
                dbContext.CanonicalDocuments.Where(c => c.ExtractionAttemptId == x.Id).Select(c => c.RawResponseFileAssetId).FirstOrDefault(),
                dbContext.CanonicalDocuments.Where(c => c.ExtractionAttemptId == x.Id).Select(c => c.GeneratedExcelFileAssetId).FirstOrDefault(),
                x.CreatedAtUtc,
                x.ClaimedAtUtc,
                x.ClaimedUntilUtc,
                x.SubmittedAtUtc,
                x.StartedAtUtc,
                x.CompletedAtUtc,
                x.UpdatedAtUtc,
                x.CompletionEventPublishedAtUtc,
                x.FailureEventPublishedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResponse<ExtractionAttemptDto>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }
}
