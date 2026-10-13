using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;

namespace DES.Modules.Ppap.Features.v1.Credits.CreateCreditTopupRequest;

public sealed class CreateCreditTopupRequestCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : ICommandHandler<CreateCreditTopupRequestCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateCreditTopupRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");
        CreditTopupRequest request = CreditTopupRequest.Create(
            tenantId,
            ownerUserId,
            command.CreditsRequested,
            command.PaymentReference,
            command.Note);
        dbContext.CreditTopupRequests.Add(request);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return request.Id;
    }
}
