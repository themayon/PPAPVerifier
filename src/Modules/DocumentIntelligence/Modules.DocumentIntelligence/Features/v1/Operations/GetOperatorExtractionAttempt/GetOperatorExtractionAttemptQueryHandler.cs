using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.v1.Operations;
using DES.Modules.DocumentIntelligence.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.DocumentIntelligence.Features.v1.Operations.GetOperatorExtractionAttempt;

public sealed class GetOperatorExtractionAttemptQueryHandler(
    DocumentIntelligenceDbContext dbContext,
    ICurrentUser currentUser) : IQueryHandler<GetOperatorExtractionAttemptQuery, OperatorExtractionAttemptDto>
{
    public async ValueTask<OperatorExtractionAttemptDto> Handle(
        GetOperatorExtractionAttemptQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (currentUser.GetUserId() == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }

        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        return await dbContext.ExtractionAttempts
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == query.Id)
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
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Extraction attempt {query.Id} not found.");
    }
}
