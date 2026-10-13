using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Data;
using Mediator;

namespace DES.Modules.Ppap.Features.v1.Reports.GetPpapReport;

public sealed class GetPpapReportQueryHandler(PpapDbContext dbContext, ICurrentUser currentUser)
    : IQueryHandler<GetPpapReportQuery, PpapReportDto>
{
    public async ValueTask<PpapReportDto> Handle(GetPpapReportQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        string tenantId = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        return await PpapReportReader.ReadAsync(dbContext, query.Id, tenantId, ownerUserId, cancellationToken).ConfigureAwait(false);
    }
}
