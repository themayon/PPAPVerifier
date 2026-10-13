using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.Services;
using DES.Modules.DocumentIntelligence.Data;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.DocumentIntelligence.Services;

internal sealed class CanonicalDocumentReader(DocumentIntelligenceDbContext dbContext) : ICanonicalDocumentReader
{
    public async Task<IReadOnlyList<CanonicalDocumentDto>> ReadAsync(
        Guid expectedOwnerUserId,
        IReadOnlyCollection<Guid> canonicalDocumentIds,
        CancellationToken cancellationToken = default)
    {
        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        if (expectedOwnerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An expected document owner is required.");
        }
        ArgumentNullException.ThrowIfNull(canonicalDocumentIds);
        if (canonicalDocumentIds.Count == 0)
        {
            throw new ArgumentException("At least one canonical document identifier is required.", nameof(canonicalDocumentIds));
        }
        if (canonicalDocumentIds.Count > ICanonicalDocumentReader.MaximumDocumentCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(canonicalDocumentIds),
                $"At most {ICanonicalDocumentReader.MaximumDocumentCount} canonical document identifiers are allowed.");
        }

        Guid[] requestedIds = [.. canonicalDocumentIds];
        if (requestedIds.Any(id => id == Guid.Empty))
        {
            throw new ArgumentException("Canonical document identifiers must be non-empty.", nameof(canonicalDocumentIds));
        }
        if (requestedIds.Distinct().Count() != requestedIds.Length)
        {
            throw new ArgumentException("Canonical document identifiers must be unique.", nameof(canonicalDocumentIds));
        }

        CanonicalDocumentProjection[] documents = await dbContext.CanonicalDocuments
            .AsNoTracking()
            .Where(document => document.TenantId == tenantId && requestedIds.Contains(document.Id))
            .Select(document => new CanonicalDocumentProjection(
                document.OwnerUserId,
                new CanonicalDocumentDto(
                    document.Id,
                    document.ExtractionAttemptId,
                    document.SchemaVersion,
                    document.CanonicalJson,
                    document.RawResponseFileAssetId,
                    document.GeneratedExcelFileAssetId,
                    document.CreatedAtUtc)))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (documents.Length != requestedIds.Length)
        {
            throw new NotFoundException("One or more canonical documents were not found in the current tenant.");
        }
        if (documents.Any(document => document.OwnerUserId != expectedOwnerUserId))
        {
            throw new ForbiddenException("One or more canonical documents do not belong to the expected owner.");
        }

        Dictionary<Guid, CanonicalDocumentDto> documentsById = documents
            .ToDictionary(document => document.Document.Id, document => document.Document);
        return requestedIds.Select(id => documentsById[id]).ToArray();
    }

    private sealed record CanonicalDocumentProjection(Guid OwnerUserId, CanonicalDocumentDto Document);
}
