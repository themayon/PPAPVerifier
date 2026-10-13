using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.Services;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.DocumentIntelligence.Services;

internal sealed class ExtractionProvenanceReader(DocumentIntelligenceDbContext dbContext) : IExtractionProvenanceReader
{
    public async Task<IReadOnlyList<ExtractionProvenanceDto>> ReadAsync(
        Guid expectedOwnerUserId,
        IReadOnlyCollection<Guid> extractionAttemptIds,
        CancellationToken cancellationToken = default)
    {
        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        if (expectedOwnerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An expected extraction owner is required.");
        }
        ArgumentNullException.ThrowIfNull(extractionAttemptIds);
        if (extractionAttemptIds.Count == 0)
        {
            throw new ArgumentException("At least one extraction attempt identifier is required.", nameof(extractionAttemptIds));
        }
        if (extractionAttemptIds.Count > IExtractionProvenanceReader.MaximumAttemptCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(extractionAttemptIds),
                $"At most {IExtractionProvenanceReader.MaximumAttemptCount} extraction attempt identifiers are allowed.");
        }

        Guid[] requestedIds = [.. extractionAttemptIds];
        if (requestedIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Extraction attempt identifiers must be non-empty.", nameof(extractionAttemptIds));
        }
        if (requestedIds.Distinct().Count() != requestedIds.Length)
        {
            throw new ArgumentException("Extraction attempt identifiers must be unique.", nameof(extractionAttemptIds));
        }

        AttemptProjection[] attempts = await dbContext.ExtractionAttempts
            .AsNoTracking()
            .Where(attempt => attempt.TenantId == tenantId && requestedIds.Contains(attempt.Id))
            .Select(attempt => new AttemptProjection(
                attempt.Id,
                attempt.OwnerUserId,
                attempt.SubmissionDocumentId,
                attempt.SourceFileAssetId,
                attempt.DocumentCode,
                attempt.SourceSha256,
                attempt.Provider,
                attempt.Model,
                attempt.ExtractorVersion,
                attempt.Status,
                attempt.CreatedAtUtc,
                attempt.CompletedAtUtc))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (attempts.Length != requestedIds.Length)
        {
            throw new NotFoundException("One or more extraction attempts were not found in the current tenant.");
        }
        if (attempts.Any(attempt => attempt.OwnerUserId != expectedOwnerUserId))
        {
            throw new ForbiddenException("One or more extraction attempts do not belong to the expected owner.");
        }
        if (attempts.Any(attempt => attempt.Status != ExtractionAttemptStatus.Succeeded || attempt.CompletedAtUtc is null))
        {
            throw new InvalidOperationException("All extraction attempts must be successfully completed.");
        }

        CanonicalProjection[] canonicalDocuments = await dbContext.CanonicalDocuments
            .AsNoTracking()
            .Where(document => document.TenantId == tenantId && requestedIds.Contains(document.ExtractionAttemptId))
            .Select(document => new CanonicalProjection(
                document.Id,
                document.ExtractionAttemptId,
                document.OwnerUserId,
                document.SchemaVersion,
                document.RawResponseFileAssetId,
                document.GeneratedExcelFileAssetId))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (canonicalDocuments.Length != requestedIds.Length)
        {
            throw new NotFoundException("One or more canonical documents were not found in the current tenant.");
        }
        if (canonicalDocuments.Any(document => document.OwnerUserId != expectedOwnerUserId))
        {
            throw new ForbiddenException("One or more canonical documents do not belong to the expected owner.");
        }

        Dictionary<Guid, AttemptProjection> attemptsById = attempts.ToDictionary(attempt => attempt.Id);
        Dictionary<Guid, CanonicalProjection> documentsByAttemptId = canonicalDocuments
            .ToDictionary(document => document.ExtractionAttemptId);
        return requestedIds.Select(id =>
        {
            AttemptProjection attempt = attemptsById[id];
            CanonicalProjection document = documentsByAttemptId[id];
            return new ExtractionProvenanceDto(
                attempt.Id,
                attempt.SubmissionDocumentId,
                attempt.SourceFileAssetId,
                attempt.DocumentCode,
                attempt.SourceSha256,
                attempt.Provider,
                attempt.Model,
                attempt.ExtractorVersion,
                attempt.Status.ToString(),
                document.Id,
                document.SchemaVersion,
                document.RawResponseFileAssetId,
                document.GeneratedExcelFileAssetId,
                attempt.CreatedAtUtc,
                attempt.CompletedAtUtc!.Value);
        }).ToArray();
    }

    private sealed record AttemptProjection(
        Guid Id,
        Guid OwnerUserId,
        Guid SubmissionDocumentId,
        Guid SourceFileAssetId,
        string DocumentCode,
        string SourceSha256,
        string Provider,
        string Model,
        string ExtractorVersion,
        ExtractionAttemptStatus Status,
        DateTime CreatedAtUtc,
        DateTime? CompletedAtUtc);

    private sealed record CanonicalProjection(
        Guid Id,
        Guid ExtractionAttemptId,
        Guid OwnerUserId,
        string SchemaVersion,
        Guid? RawResponseFileAssetId,
        Guid? GeneratedExcelFileAssetId);
}
