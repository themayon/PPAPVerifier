using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Submissions.AddSubmissionDocument;

public sealed class AddSubmissionDocumentCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : ICommandHandler<AddSubmissionDocumentCommand, Guid>
{
    public async ValueTask<Guid> Handle(
        AddSubmissionDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        _ = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        PpapSubmission submission = await dbContext.PpapSubmissions
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(
                x => x.Id == command.SubmissionId && x.OwnerUserId == ownerUserId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP submission {command.SubmissionId} not found.");

        PpapSubmissionDocument document = submission.AddDocumentSlot(command.DocumentCode);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return document.Id;
    }
}
