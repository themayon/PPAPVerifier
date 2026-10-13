using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Submissions.GetPpapSubmission;

public sealed class GetPpapSubmissionQueryHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<GetPpapSubmissionQuery, PpapSubmissionDto>
{
    public async ValueTask<PpapSubmissionDto> Handle(
        GetPpapSubmissionQuery query,
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

        return await dbContext.PpapSubmissions
            .AsNoTracking()
            .Where(x => x.Id == query.Id && x.OwnerUserId == ownerUserId)
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
                x.CancelledAtUtc))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP submission {query.Id} not found.");
    }
}
