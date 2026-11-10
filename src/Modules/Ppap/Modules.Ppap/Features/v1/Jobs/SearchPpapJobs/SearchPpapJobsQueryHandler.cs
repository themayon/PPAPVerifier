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
        IQueryable<JobRow> rows =
            from job in jobs
            join submission in submissions on job.SubmissionId equals submission.Id
            join report in reports on job.Id equals report.JobId into jobReports
            from report in jobReports.DefaultIfEmpty()
            select new JobRow
            {
                Job = job,
                PartName = submission.PartName,
                PartNumber = submission.PartNumber,
                ReportId = report == null ? null : report.Id
            };

        long totalCount = await rows.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<PpapAdminJobDto> items = await ApplySort(rows, query.Sort)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new PpapAdminJobDto(
                x.Job.Id,
                x.Job.SubmissionId,
                x.Job.SubmissionVersion,
                x.PartName,
                x.PartNumber,
                x.Job.OwnerUserId,
                x.Job.Status.ToString(),
                x.Job.CurrentStage.ToString(),
                x.Job.ProgressPercent,
                x.Job.CreatedAtUtc,
                x.Job.StartedAtUtc,
                x.Job.CompletedAtUtc,
                x.Job.FailedAtUtc,
                x.Job.ErrorCode,
                x.Job.ErrorMessage,
                x.ReportId))
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

    private static IOrderedQueryable<JobRow> ApplySort(
        IQueryable<JobRow> rows,
        string? sort) => sort?.ToUpperInvariant() switch
        {
            "CREATEDATUTC" => rows.OrderBy(x => x.Job.CreatedAtUtc).ThenBy(x => x.Job.Id),
            "STATUS" => rows.OrderBy(x => x.Job.Status).ThenBy(x => x.Job.Id),
            "-STATUS" => rows.OrderByDescending(x => x.Job.Status).ThenByDescending(x => x.Job.Id),
            "PROGRESSPERCENT" => rows.OrderBy(x => x.Job.ProgressPercent).ThenBy(x => x.Job.Id),
            "-PROGRESSPERCENT" => rows.OrderByDescending(x => x.Job.ProgressPercent).ThenByDescending(x => x.Job.Id),
            "PARTNAME" => rows.OrderBy(x => x.PartName).ThenBy(x => x.Job.Id),
            "-PARTNAME" => rows.OrderByDescending(x => x.PartName).ThenByDescending(x => x.Job.Id),
            "PARTNUMBER" => rows.OrderBy(x => x.PartNumber).ThenBy(x => x.Job.Id),
            "-PARTNUMBER" => rows.OrderByDescending(x => x.PartNumber).ThenByDescending(x => x.Job.Id),
            "OWNERUSERID" => rows.OrderBy(x => x.Job.OwnerUserId).ThenBy(x => x.Job.Id),
            "-OWNERUSERID" => rows.OrderByDescending(x => x.Job.OwnerUserId).ThenByDescending(x => x.Job.Id),
            _ => rows.OrderByDescending(x => x.Job.CreatedAtUtc).ThenByDescending(x => x.Job.Id)
        };

    private sealed class JobRow
    {
        public PpapJob Job { get; init; } = default!;
        public string PartName { get; init; } = default!;
        public string PartNumber { get; init; } = default!;
        public Guid? ReportId { get; init; }
    }
}
