using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.v1.Jobs;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Jobs.SearchPpapJobs;

public sealed class SearchPpapJobsQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<SearchPpapJobsQuery, PagedResponse<PpapAdminJobDto>>
{
    public async ValueTask<PagedResponse<PpapAdminJobDto>> Handle(
        SearchPpapJobsQuery query,
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

        IQueryable<PpapJob> jobs = dbContext.PpapJobs
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse(query.Status, true, out PpapJobStatus status))
        {
            jobs = jobs.Where(x => x.Status == status);
        }

        IQueryable<PpapSubmission> submissions = dbContext.PpapSubmissions
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{query.Search.Trim()}%";
            submissions = submissions.Where(x =>
                EF.Functions.ILike(x.PartName, pattern) ||
                EF.Functions.ILike(x.PartNumber, pattern));
        }

        IQueryable<PpapReport> reports = dbContext.PpapReports
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId);
        IQueryable<PpapAdminJobDto> results =
            from job in jobs
            join submission in submissions on job.SubmissionId equals submission.Id
            join report in reports on job.Id equals report.JobId into jobReports
            from report in jobReports.DefaultIfEmpty()
            select new PpapAdminJobDto(
                job.Id,
                job.SubmissionId,
                job.SubmissionVersion,
                submission.PartName,
                submission.PartNumber,
                job.OwnerUserId,
                job.Status.ToString(),
                job.CurrentStage.ToString(),
                job.ProgressPercent,
                job.CreatedAtUtc,
                job.StartedAtUtc,
                job.CompletedAtUtc,
                job.FailedAtUtc,
                job.ErrorCode,
                job.ErrorMessage,
                report == null ? null : report.Id);

        long totalCount = await results.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<PpapAdminJobDto> items = await ApplySort(results, query.Sort)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResponse<PpapAdminJobDto>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
        };
    }

    private static IOrderedQueryable<PpapAdminJobDto> ApplySort(
        IQueryable<PpapAdminJobDto> jobs,
        string? sort) => sort?.ToUpperInvariant() switch
        {
            "CREATEDATUTC" => jobs.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            "STATUS" => jobs.OrderBy(x => x.Status).ThenBy(x => x.Id),
            "-STATUS" => jobs.OrderByDescending(x => x.Status).ThenByDescending(x => x.Id),
            "PROGRESSPERCENT" => jobs.OrderBy(x => x.ProgressPercent).ThenBy(x => x.Id),
            "-PROGRESSPERCENT" => jobs.OrderByDescending(x => x.ProgressPercent).ThenByDescending(x => x.Id),
            "PARTNAME" => jobs.OrderBy(x => x.PartName).ThenBy(x => x.Id),
            "-PARTNAME" => jobs.OrderByDescending(x => x.PartName).ThenByDescending(x => x.Id),
            "PARTNUMBER" => jobs.OrderBy(x => x.PartNumber).ThenBy(x => x.Id),
            "-PARTNUMBER" => jobs.OrderByDescending(x => x.PartNumber).ThenByDescending(x => x.Id),
            "OWNERUSERID" => jobs.OrderBy(x => x.OwnerUserId).ThenBy(x => x.Id),
            "-OWNERUSERID" => jobs.OrderByDescending(x => x.OwnerUserId).ThenByDescending(x => x.Id),
            _ => jobs.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
        };
}
