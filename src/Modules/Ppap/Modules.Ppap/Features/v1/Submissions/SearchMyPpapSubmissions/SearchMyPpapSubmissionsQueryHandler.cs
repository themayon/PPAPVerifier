using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Submissions.SearchMyPpapSubmissions;

public sealed class SearchMyPpapSubmissionsQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<SearchMyPpapSubmissionsQuery, PagedResponse<PpapSubmissionDto>>
{
    public async ValueTask<PagedResponse<PpapSubmissionDto>> Handle(
        SearchMyPpapSubmissionsQuery query,
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
        IQueryable<PpapSubmission> submissions = dbContext.PpapSubmissions
            .AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{query.Search.Trim()}%";
            submissions = submissions.Where(x =>
                EF.Functions.ILike(x.PartName, pattern) ||
                EF.Functions.ILike(x.PartNumber, pattern) ||
                EF.Functions.ILike(x.DrawingRevision, pattern));
        }
        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse(query.Status, true, out SubmissionStatus status))
        {
            submissions = submissions.Where(x => x.Status == status);
        }

        long totalCount = await submissions.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<PpapSubmissionDto> items = await ApplySort(submissions, query.Sort)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PpapSubmissionDto(
                x.Id,
                x.PartId,
                x.PartName,
                x.PartNumber,
                x.DrawingRevision,
                x.PpapLevel,
                x.RuleTemplateId,
                x.RuleTemplateVersionId,
                x.Status.ToString(),
                x.Version,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.SubmittedAtUtc,
                x.CancelledAtUtc,
                x.Decision.ToString(),
                x.DecisionComment,
                x.DecidedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResponse<PpapSubmissionDto>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static IOrderedQueryable<PpapSubmission> ApplySort(
        IQueryable<PpapSubmission> submissions,
        string? sort) => sort?.ToUpperInvariant() switch
        {
            "CREATEDAT" => submissions.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            "UPDATEDAT" => submissions.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            "-UPDATEDAT" => submissions.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id),
            "PARTNAME" => submissions.OrderBy(x => x.PartName).ThenBy(x => x.Id),
            "-PARTNAME" => submissions.OrderByDescending(x => x.PartName).ThenByDescending(x => x.Id),
            "PARTNUMBER" => submissions.OrderBy(x => x.PartNumber).ThenBy(x => x.Id),
            "-PARTNUMBER" => submissions.OrderByDescending(x => x.PartNumber).ThenByDescending(x => x.Id),
            "DRAWINGREVISION" => submissions.OrderBy(x => x.DrawingRevision).ThenBy(x => x.Id),
            "-DRAWINGREVISION" => submissions.OrderByDescending(x => x.DrawingRevision).ThenByDescending(x => x.Id),
            "PPAPLEVEL" => submissions.OrderBy(x => x.PpapLevel).ThenBy(x => x.Id),
            "-PPAPLEVEL" => submissions.OrderByDescending(x => x.PpapLevel).ThenByDescending(x => x.Id),
            "STATUS" => submissions.OrderBy(x => x.Status).ThenBy(x => x.Id),
            "-STATUS" => submissions.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            _ => submissions.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
        };
}
