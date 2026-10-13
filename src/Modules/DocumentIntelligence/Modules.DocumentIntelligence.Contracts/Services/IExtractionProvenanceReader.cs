using DES.Modules.DocumentIntelligence.Contracts.Dtos;

namespace DES.Modules.DocumentIntelligence.Contracts.Services;

public interface IExtractionProvenanceReader
{
    public const int MaximumAttemptCount = 100;

    Task<IReadOnlyList<ExtractionProvenanceDto>> ReadAsync(
        Guid expectedOwnerUserId,
        IReadOnlyCollection<Guid> extractionAttemptIds,
        CancellationToken cancellationToken = default);
}
