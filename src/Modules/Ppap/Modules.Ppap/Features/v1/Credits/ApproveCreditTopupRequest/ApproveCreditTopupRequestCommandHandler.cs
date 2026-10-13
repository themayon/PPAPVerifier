using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Credits;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DES.Modules.Ppap.Features.v1.Credits.ApproveCreditTopupRequest;

public sealed class ApproveCreditTopupRequestCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : ICommandHandler<ApproveCreditTopupRequestCommand, Unit>
{
    public async ValueTask<Unit> Handle(ApproveCreditTopupRequestCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid decisionByUserId = currentUser.GetUserId();
        if (decisionByUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        try
        {
            await ApproveAsync(command, tenantId, decisionByUserId, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_CreditAccounts_TenantId_OwnerUserId"
            })
        {
            dbContext.ChangeTracker.Clear();
            await ApproveAsync(command, tenantId, decisionByUserId, cancellationToken).ConfigureAwait(false);
        }

        return Unit.Value;
    }

    private async Task ApproveAsync(
        ApproveCreditTopupRequestCommand command,
        string tenantId,
        Guid decisionByUserId,
        CancellationToken cancellationToken)
    {
        CreditTopupRequest request = await dbContext.CreditTopupRequests
            .FirstOrDefaultAsync(x => x.Id == command.Id && x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Credit top-up request {command.Id} not found.");
        CreditAccount? account = await dbContext.CreditAccounts
            .Include(x => x.Transactions)
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId && x.OwnerUserId == request.OwnerUserId,
                cancellationToken)
            .ConfigureAwait(false);
        if (account is null)
        {
            account = CreditAccount.Create(tenantId, request.OwnerUserId);
            dbContext.CreditAccounts.Add(account);
        }

        account.TopUp(request.CreditsRequested, $"topup:{request.Id:N}");
        request.Approve(decisionByUserId, command.DecisionNote);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
