using Mediator;

namespace DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;

public sealed record RequestDocumentExtractionCommand(
    Guid OwnerUserId,
    Guid SubmissionDocumentId,
    Guid SourceFileAssetId,
    string DocumentCode) : ICommand<Guid>;
