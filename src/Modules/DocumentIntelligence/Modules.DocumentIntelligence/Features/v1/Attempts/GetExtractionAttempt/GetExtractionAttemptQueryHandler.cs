using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using DES.Modules.DocumentIntelligence.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.DocumentIntelligence.Features.v1.Attempts.GetExtractionAttempt;

public sealed class GetExtractionAttemptQueryHandler(
    DocumentIntelligenceDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<GetExtractionAttemptQuery, ExtractionAttemptDto>
{
    public async ValueTask<ExtractionAttemptDto> Handle(
        GetExtractionAttemptQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        _ = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");

        return await dbContext.ExtractionAttempts
            .AsNoTracking()
            .Where(x => x.Id == query.Id && x.OwnerUserId == ownerUserId)
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
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Extraction attempt {query.Id} not found.");
    }
}
