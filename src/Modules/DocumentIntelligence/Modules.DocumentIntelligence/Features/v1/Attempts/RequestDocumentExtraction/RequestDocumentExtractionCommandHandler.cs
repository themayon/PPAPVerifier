using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.DocumentIntelligence.Contracts.v1.Attempts;
using Mediator;

namespace DES.Modules.DocumentIntelligence.Features.v1.Attempts.RequestDocumentExtraction;

public sealed class RequestDocumentExtractionCommandHandler(
    ICurrentUser currentUser,
    IDocumentExtractionRequestService requestService) : ICommandHandler<RequestDocumentExtractionCommand, Guid>
{
    public async ValueTask<Guid> Handle(
        RequestDocumentExtractionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid currentUserId = currentUser.GetUserId();
        if (currentUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        if (command.OwnerUserId != currentUserId)
        {
            throw new ForbiddenException("Document extraction can only be requested by the owning user.");
        }

        return await requestService.RequestAsync(
            command.OwnerUserId,
            command.SubmissionDocumentId,
            command.SourceFileAssetId,
            command.DocumentCode,
            cancellationToken).ConfigureAwait(false);
    }
}
