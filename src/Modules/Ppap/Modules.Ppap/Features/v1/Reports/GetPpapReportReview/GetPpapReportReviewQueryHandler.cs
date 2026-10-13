using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Data;
using Mediator;

namespace DES.Modules.Ppap.Features.v1.Reports.GetPpapReportReview;

public sealed class GetPpapReportReviewQueryHandler(PpapDbContext dbContext, ICurrentUser currentUser)
    : IQueryHandler<GetPpapReportReviewQuery, PpapReportDto>
{
    public async ValueTask<PpapReportDto> Handle(GetPpapReportReviewQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (currentUser.GetUserId() == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        string tenantId = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        return await PpapReportReader.ReadAsync(dbContext, query.Id, tenantId, null, cancellationToken).ConfigureAwait(false);
    }
}
