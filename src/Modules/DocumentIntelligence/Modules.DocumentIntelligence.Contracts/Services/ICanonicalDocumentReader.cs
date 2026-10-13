using DES.Modules.DocumentIntelligence.Contracts.Dtos;

namespace DES.Modules.DocumentIntelligence.Contracts.Services;

public interface ICanonicalDocumentReader
{
    public const int MaximumDocumentCount = 100;

    Task<IReadOnlyList<CanonicalDocumentDto>> ReadAsync(
        Guid expectedOwnerUserId,
        IReadOnlyCollection<Guid> canonicalDocumentIds,
        CancellationToken cancellationToken = default);
}
