using System.Diagnostics;
using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Eventing.Abstractions;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DES.Modules.Ppap.Features.v1.Submissions.SubmitDraftSubmission;

public sealed class SubmitDraftSubmissionCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser,
    IOutboxWriter outbox) : ICommandHandler<SubmitDraftSubmissionCommand, Guid>
{
    private const int SubmissionCreditCost = 10;

    public async ValueTask<Guid> Handle(
        SubmitDraftSubmissionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            PpapSubmission submission = await dbContext.PpapSubmissions
                .Include(x => x.Documents)
                .FirstOrDefaultAsync(
                    x => x.Id == command.Id
                        && x.TenantId == tenantId
                        && x.OwnerUserId == ownerUserId,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new NotFoundException($"PPAP submission {command.Id} not found.");
            if (submission.Version != command.ExpectedVersion)
            {
                throw ConcurrencyConflict(command.ExpectedVersion, submission.Version);
            }

            CreditAccount account = await dbContext.CreditAccounts
                .Include(x => x.Transactions)
                .FirstOrDefaultAsync(
                    x => x.TenantId == tenantId && x.OwnerUserId == ownerUserId,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw Conflict("A PPAP credit account with at least 10 available credits is required.");
            if (account.AvailableCredits < SubmissionCreditCost)
            {
                throw Conflict("Insufficient available credits to submit this PPAP submission.");
            }

            string reservationReference = $"submission:{submission.Id:N}:v{command.ExpectedVersion}";
            account.Reserve(SubmissionCreditCost, reservationReference);
            submission.Submit();
            PpapJob job = PpapJob.CreateQueued(
                tenantId,
                submission.Id,
                submission.Version,
                ownerUserId);
            dbContext.PpapJobs.Add(job);

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await outbox.AddAsync(new PpapSubmissionQueuedIntegrationEvent(
                Id: Guid.CreateVersion7(),
                OccurredOnUtc: DateTime.UtcNow,
                TenantId: tenantId,
                CorrelationId: Activity.Current?.Id ?? Guid.CreateVersion7().ToString(),
                Source: "Ppap",
                SubmissionId: submission.Id,
                JobId: job.Id,
                SubmissionVersion: submission.Version,
                OwnerUserId: ownerUserId), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return job.Id;
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrencyConflict(command.ExpectedVersion);
        }
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);

    private static CustomException ConcurrencyConflict(int expectedVersion, int? currentVersion = null) =>
        new(
            currentVersion.HasValue
                ? $"PPAP submission version conflict. Expected version {expectedVersion}, but current version is {currentVersion.Value}."
                : $"PPAP submission version conflict. Expected version {expectedVersion}; the submission was modified by another request.",
            (IEnumerable<string>?)null,
            HttpStatusCode.Conflict);
}
