using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Reports.GetPpapReportReview;

public sealed class GetPpapReportReviewQueryHandler(PpapDbContext dbContext, ICurrentUser currentUser)
    : IQueryHandler<GetPpapReportReviewQuery, PpapReportDto>
{
    public async ValueTask<PpapReportDto> Handle(GetPpapReportReviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (currentUser.GetUserId() == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        string tenantId = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        PpapReportDto report = await PpapReportReader.ReadAsync(dbContext, query.Id, tenantId, null, cancellationToken).ConfigureAwait(false);
        PpapReportSubmissionStateDto? submission = await dbContext.PpapSubmissions.AsNoTracking()
            .Where(x => x.Id == report.SubmissionId)
            .Select(x => new PpapReportSubmissionStateDto(
                x.Id, x.Status.ToString(), x.Decision.ToString(), x.DecisionComment, x.DecidedByUserId, x.DecidedAtUtc, x.Version,
                !dbContext.PpapReports.Any(r => r.SubmissionId == x.Id && r.GeneratedAtUtc > report.GeneratedAtUtc)))
            .SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        return report with { Submission = submission };
    }
}
