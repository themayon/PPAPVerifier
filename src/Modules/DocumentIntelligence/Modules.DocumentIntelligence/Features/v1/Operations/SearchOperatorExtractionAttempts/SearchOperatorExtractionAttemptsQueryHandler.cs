using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.v1.Operations;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.DocumentIntelligence.Features.v1.Operations.SearchOperatorExtractionAttempts;

public sealed class SearchOperatorExtractionAttemptsQueryHandler(
    DocumentIntelligenceDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<SearchOperatorExtractionAttemptsQuery, PagedResponse<OperatorExtractionAttemptDto>>
{
    public async ValueTask<PagedResponse<OperatorExtractionAttemptDto>> Handle(
        SearchOperatorExtractionAttemptsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (currentUser.GetUserId() == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }

        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        int pageNumber = query.PageNumber ?? 1;
        int pageSize = query.PageSize ?? 20;
        IQueryable<ExtractionAttempt> attempts = dbContext.ExtractionAttempts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse(query.Status, true, out ExtractionAttemptStatus status))
        {
            attempts = attempts.Where(x => x.Status == status);
        }
        if (!string.IsNullOrWhiteSpace(query.DocumentCode))
        {
            string documentCode = query.DocumentCode.Trim();
            attempts = attempts.Where(x => x.DocumentCode == documentCode);
        }
        if (query.OwnerUserId.HasValue)
        {
            attempts = attempts.Where(x => x.OwnerUserId == query.OwnerUserId.Value);
        }

        long totalCount = await attempts.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<OperatorExtractionAttemptDto> items = await ApplySort(attempts, query.Sort)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new OperatorExtractionAttemptDto(
                x.Id,
                x.OwnerUserId,
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
                dbContext.CanonicalDocuments
                    .Where(c => c.TenantId == tenantId && c.ExtractionAttemptId == x.Id)
                    .Select(c => (Guid?)c.Id)
                    .FirstOrDefault(),
                dbContext.CanonicalDocuments
                    .Where(c => c.TenantId == tenantId && c.ExtractionAttemptId == x.Id)
                    .Select(c => c.RawResponseFileAssetId)
                    .FirstOrDefault(),
                dbContext.CanonicalDocuments
                    .Where(c => c.TenantId == tenantId && c.ExtractionAttemptId == x.Id)
                    .Select(c => c.GeneratedExcelFileAssetId)
                    .FirstOrDefault(),
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

        return new PagedResponse<OperatorExtractionAttemptDto>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static IOrderedQueryable<ExtractionAttempt> ApplySort(
        IQueryable<ExtractionAttempt> attempts,
        string? sort) => sort?.ToUpperInvariant() switch
        {
            "CREATEDAT" => attempts.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            "UPDATEDAT" => attempts.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            "-UPDATEDAT" => attempts.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id),
            "STATUS" => attempts.OrderBy(x => x.Status).ThenBy(x => x.Id),
            "-STATUS" => attempts.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            "DOCUMENTCODE" => attempts.OrderBy(x => x.DocumentCode).ThenBy(x => x.Id),
            "-DOCUMENTCODE" => attempts.OrderByDescending(x => x.DocumentCode).ThenByDescending(x => x.Id),
            "OWNERUSERID" => attempts.OrderBy(x => x.OwnerUserId).ThenBy(x => x.Id),
            "-OWNERUSERID" => attempts.OrderByDescending(x => x.OwnerUserId).ThenByDescending(x => x.Id),
            _ => attempts.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
        };
}
