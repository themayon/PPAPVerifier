using System.Diagnostics;
using DES.Framework.Core.Exceptions;
using DES.Framework.Eventing.Abstractions;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DES.Modules.Ppap.Processing;

public sealed class PpapProcessingJob(
    IMultiTenantStore<AppTenantInfo> tenantStore,
    IMultiTenantContextSetter tenantContextSetter,
    IServiceScopeFactory scopeFactory,
    IOptions<PpapProcessingOptions> options,
    ILogger<PpapProcessingJob> logger)
{
    private const int SubmissionCreditCost = 10;
    private readonly TimeSpan _leaseDuration = TimeSpan.FromMinutes(options.Value.LeaseMinutes);
    private readonly PpapProcessingMode _mode = options.Value.Mode;

    public async Task RunAsync(string tenantId, Guid jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (jobId == Guid.Empty) throw new ArgumentException("Job ID is required.", nameof(jobId));

        AppTenantInfo tenant = await tenantStore.GetAsync(tenantId).ConfigureAwait(false)
            ?? throw new NotFoundException($"Tenant {tenantId} not found for PPAP job {jobId}.");
        tenantContextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(tenant);

        using IServiceScope scope = scopeFactory.CreateScope();
        PpapDbContext dbContext = scope.ServiceProvider.GetRequiredService<PpapDbContext>();
        IPpapProcessingProvider provider = scope.ServiceProvider.GetRequiredService<IPpapProcessingProvider>();
        IPpapProgressNotifier notifier = scope.ServiceProvider.GetRequiredService<IPpapProgressNotifier>();
        IOutboxWriter outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        DateTime claimedAtUtc = DateTime.UtcNow;
        Guid leaseId = Guid.CreateVersion7();
        DateTime leaseExpiresAtUtc = claimedAtUtc.Add(_leaseDuration);
        int claimed = await dbContext.PpapJobs
            .Where(x => x.Id == jobId
                && x.TenantId == tenantId
                && (x.Status == PpapJobStatus.Queued
                    || (x.Status == PpapJobStatus.Processing
                        && (x.ProcessingLeaseId == null
                            || x.ProcessingLeaseExpiresAtUtc == null
                            || x.ProcessingLeaseExpiresAtUtc <= claimedAtUtc))))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, PpapJobStatus.Processing)
                .SetProperty(x => x.StartedAtUtc, x => x.StartedAtUtc ?? claimedAtUtc)
                .SetProperty(x => x.ProcessingLeaseId, leaseId)
                .SetProperty(x => x.ProcessingLeaseExpiresAtUtc, leaseExpiresAtUtc),
                cancellationToken)
            .ConfigureAwait(false);

        dbContext.ChangeTracker.Clear();
        PpapJob job = await dbContext.PpapJobs
            .FirstOrDefaultAsync(x => x.Id == jobId && x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP job {jobId} not found.");

        if (claimed == 0)
        {
            await HandleUnclaimedAsync(dbContext, outbox, job, tenantId, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            PpapSubmission submission = await dbContext.PpapSubmissions
                .Include(x => x.Documents)
                .FirstOrDefaultAsync(
                    x => x.Id == job.SubmissionId
                        && x.TenantId == tenantId
                        && x.OwnerUserId == job.OwnerUserId
                        && x.Version == job.SubmissionVersion,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new NotFoundException($"PPAP submission {job.SubmissionId} not found for job {jobId}.");
            CreditAccount account = await dbContext.CreditAccounts
                .Include(x => x.Transactions)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.OwnerUserId == job.OwnerUserId, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new NotFoundException($"PPAP credit account for job {jobId} not found.");

            string operationReference = OperationReference(submission);
            account.CommitReservation(SubmissionCreditCost, operationReference);
            if (_mode == PpapProcessingMode.Real)
            {
                await PublishExtractionRequestsAsync(
                    dbContext,
                    outbox,
                    job,
                    submission,
                    leaseId,
                    cancellationToken).ConfigureAwait(false);
                await NotifyProgressAsync(notifier, job, tenantId, cancellationToken).ConfigureAwait(false);
                return;
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await NotifyProgressAsync(notifier, job, tenantId, cancellationToken).ConfigureAwait(false);

            PpapProcessingResult result = await provider.ProcessAsync(
                new PpapProcessingRequest(tenantId, job.Id, submission.Id, submission.Version, submission.OwnerUserId),
                cancellationToken).ConfigureAwait(false);

            if (!result.Succeeded)
            {
                await FailClaimedAsync(
                    dbContext,
                    outbox,
                    job,
                    submission,
                    account,
                    operationReference,
                    result.ErrorCode ?? "PROCESSING_FAILED",
                    result.ErrorMessage ?? "PPAP processing failed.",
                    cancellationToken).ConfigureAwait(false);
                await NotifyProgressAsync(notifier, job, tenantId, cancellationToken).ConfigureAwait(false);
                logger.LogWarning("PPAP processing job {JobId} for tenant {TenantId} failed with code {ErrorCode}", jobId, tenantId, job.ErrorCode);
                return;
            }

            while (job.CurrentStage != PpapJobStage.NotificationDelivery)
            {
                PpapJobStage nextStage = (PpapJobStage)((int)job.CurrentStage + 1);
                job.AdvanceStage(nextStage, ProgressFor(nextStage));
                job.RenewProcessingLease(leaseId, DateTime.UtcNow.Add(_leaseDuration));
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await NotifyProgressAsync(notifier, job, tenantId, cancellationToken).ConfigureAwait(false);
            }

            await using (IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
            {
                PpapReport? report = await dbContext.PpapReports
                    .FirstOrDefaultAsync(x => x.JobId == job.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (report is null)
                {
                    report = PpapReport.CreateFixture(
                        tenantId,
                        submission.Id,
                        job.Id,
                        submission.Version,
                        submission.OwnerUserId,
                        submission.Documents.Count(x => x.Status == SubmissionDocumentStatus.Available),
                        submission.Documents.Count(x => x.Status == SubmissionDocumentStatus.NotApplicable),
                        submission.Documents.Count(x => x.Status == SubmissionDocumentStatus.Quarantined),
                        0,
                        0,
                        0,
                        0);
                    dbContext.PpapReports.Add(report);
                }
                job.Complete();
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await AddCompletionEventAsync(outbox, job, submission, report, cancellationToken).ConfigureAwait(false);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            await NotifyProgressAsync(notifier, job, tenantId, cancellationToken).ConfigureAwait(false);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("PPAP processing job {JobId} for tenant {TenantId} completed", jobId, tenantId);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "PPAP processing failed for job {JobId} and tenant {TenantId}", jobId, tenantId);
            if (_mode == PpapProcessingMode.Real)
            {
                await ReleaseProcessingLeaseForRetryAsync(dbContext, jobId, tenantId, leaseId).ConfigureAwait(false);
                throw;
            }

            bool failed = await TryFailUnexpectedAsync(dbContext, outbox, jobId, tenantId, leaseId).ConfigureAwait(false);
            if (!failed)
            {
                throw;
            }
        }
    }

    private static async Task ReleaseProcessingLeaseForRetryAsync(
        PpapDbContext dbContext,
        Guid jobId,
        string tenantId,
        Guid leaseId)
    {
        dbContext.ChangeTracker.Clear();
        await dbContext.PpapJobs
            .Where(x => x.Id == jobId
                && x.TenantId == tenantId
                && x.Status == PpapJobStatus.Processing
                && x.ProcessingLeaseId == leaseId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.ProcessingLeaseId, (Guid?)null)
                .SetProperty(x => x.ProcessingLeaseExpiresAtUtc, (DateTime?)null),
                CancellationToken.None)
            .ConfigureAwait(false);
    }

    private async Task PublishExtractionRequestsAsync(
        PpapDbContext dbContext,
        IOutboxWriter outbox,
        PpapJob job,
        PpapSubmission submission,
        Guid leaseId,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        if (job.CurrentStage == PpapJobStage.UploadVerification)
        {
            job.AdvanceStage(PpapJobStage.PreprocessingExtraction, ProgressFor(PpapJobStage.PreprocessingExtraction));
        }
        else if (job.CurrentStage != PpapJobStage.PreprocessingExtraction)
        {
            throw new InvalidOperationException($"PPAP job {job.Id} cannot request extraction from stage {job.CurrentStage}.");
        }
        job.RenewProcessingLease(leaseId, DateTime.UtcNow.Add(_leaseDuration));

        foreach (PpapSubmissionDocument document in submission.Documents.Where(
            document => document.Status == SubmissionDocumentStatus.Available
                && document.FileAssetId.HasValue
                && document.ExtractionRequestPublishedAtUtc is null))
        {
            Guid fileAssetId = document.FileAssetId.GetValueOrDefault();
            Guid eventId = document.ExtractionRequestEventId ?? Guid.CreateVersion7();
            DateTime occurredOnUtc = DateTime.UtcNow;
            if (!document.MarkExtractionRequestPublished(eventId, occurredOnUtc))
            {
                continue;
            }

            await outbox.AddAsync(new PpapDocumentExtractionRequestedIntegrationEvent(
                eventId,
                occurredOnUtc,
                job.TenantId,
                Activity.Current?.Id ?? eventId.ToString(),
                "Ppap",
                job.Id,
                submission.Id,
                document.Id,
                submission.OwnerUserId,
                fileAssetId,
                document.DocumentCode), cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task HandleUnclaimedAsync(PpapDbContext dbContext, IOutboxWriter outbox, PpapJob job, string tenantId, CancellationToken cancellationToken)
    {
        if (job.Status is PpapJobStatus.Queued or PpapJobStatus.Processing or PpapJobStatus.Cancelled)
        {
            return;
        }

        PpapSubmission submission = await dbContext.PpapSubmissions
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(
                x => x.Id == job.SubmissionId
                    && x.TenantId == tenantId
                    && x.OwnerUserId == job.OwnerUserId
                    && x.Version == job.SubmissionVersion,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP submission {job.SubmissionId} not found for job {job.Id}.");

        if (job.Status == PpapJobStatus.Completed)
        {
            PpapReport report = await dbContext.PpapReports
                .FirstOrDefaultAsync(x => x.JobId == job.Id, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new NotFoundException($"PPAP report for job {job.Id} not found.");
            await PublishCompletionAsync(dbContext, outbox, job, submission, report, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (job.Status == PpapJobStatus.Failed)
        {
            await PublishFailureAsync(dbContext, outbox, job, submission, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task FailClaimedAsync(
        PpapDbContext dbContext,
        IOutboxWriter outbox,
        PpapJob job,
        PpapSubmission submission,
        CreditAccount account,
        string operationReference,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        job.Fail(errorCode, errorMessage);
        RefundCommittedReservation(account, operationReference);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AddFailureEventAsync(outbox, job, submission, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> TryFailUnexpectedAsync(PpapDbContext dbContext, IOutboxWriter outbox, Guid jobId, string tenantId, Guid leaseId)
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            PpapJob? job = await dbContext.PpapJobs
                .FirstOrDefaultAsync(x => x.Id == jobId && x.TenantId == tenantId, CancellationToken.None)
                .ConfigureAwait(false);
            if (job is null || job.Status != PpapJobStatus.Processing || job.ProcessingLeaseId != leaseId)
            {
                return job?.Status is PpapJobStatus.Completed or PpapJobStatus.Failed or PpapJobStatus.Cancelled;
            }

            PpapSubmission? submission = await dbContext.PpapSubmissions
                .Include(x => x.Documents)
                .FirstOrDefaultAsync(
                    x => x.Id == job.SubmissionId
                        && x.TenantId == tenantId
                        && x.OwnerUserId == job.OwnerUserId
                        && x.Version == job.SubmissionVersion,
                    CancellationToken.None)
                .ConfigureAwait(false);
            CreditAccount? account = await dbContext.CreditAccounts
                .Include(x => x.Transactions)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.OwnerUserId == job.OwnerUserId, CancellationToken.None)
                .ConfigureAwait(false);

            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            job.Fail("PROCESSING_ERROR", "PPAP processing failed unexpectedly.");
            if (account is not null)
            {
                string operationReference = submission is null
                    ? $"submission:{job.SubmissionId:N}:v{job.SubmissionVersion - 1}"
                    : OperationReference(submission);
                RefundCommittedReservation(account, operationReference);
            }
            await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            if (submission is not null)
            {
                await AddFailureEventAsync(outbox, job, submission, CancellationToken.None).ConfigureAwait(false);
                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            }
            await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception failureException)
        {
            logger.LogError(failureException, "Failed to persist terminal failure for PPAP job {JobId} and tenant {TenantId}", jobId, tenantId);
            return false;
        }
    }

    private static void RefundCommittedReservation(CreditAccount account, string operationReference)
    {
        if (account.Transactions.Any(x => x.Kind == CreditTransactionKind.Commit
            && x.Reference == operationReference
            && x.Amount == SubmissionCreditCost))
        {
            account.Refund(SubmissionCreditCost, operationReference);
        }
    }

    private static string OperationReference(PpapSubmission submission) =>
        $"submission:{submission.Id:N}:v{submission.Version - 1}";

    private static async Task PublishCompletionAsync(PpapDbContext dbContext, IOutboxWriter outbox, PpapJob job, PpapSubmission submission, PpapReport report, CancellationToken cancellationToken)
    {
        if (job.CompletionEventPublishedAtUtc is not null) return;
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await AddCompletionEventAsync(outbox, job, submission, report, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PublishFailureAsync(PpapDbContext dbContext, IOutboxWriter outbox, PpapJob job, PpapSubmission submission, CancellationToken cancellationToken)
    {
        if (job.FailureEventPublishedAtUtc is not null) return;
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await AddFailureEventAsync(outbox, job, submission, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddCompletionEventAsync(IOutboxWriter outbox, PpapJob job, PpapSubmission submission, PpapReport report, CancellationToken cancellationToken)
    {
        Guid eventId = job.CompletionEventId ?? Guid.CreateVersion7();
        if (!job.MarkCompletionEventPublished(eventId)) return;
        await outbox.AddAsync(new PpapProcessingCompletedIntegrationEvent(
            eventId, DateTime.UtcNow, job.TenantId, Activity.Current?.Id ?? eventId.ToString(), "Ppap",
            job.Id, submission.Id, report.Id, submission.Version, submission.OwnerUserId, submission.PartName, submission.PartNumber), cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddFailureEventAsync(IOutboxWriter outbox, PpapJob job, PpapSubmission submission, CancellationToken cancellationToken)
    {
        Guid eventId = job.FailureEventId ?? Guid.CreateVersion7();
        if (!job.MarkFailureEventPublished(eventId)) return;
        await outbox.AddAsync(new PpapProcessingFailedIntegrationEvent(
            eventId, DateTime.UtcNow, job.TenantId, Activity.Current?.Id ?? eventId.ToString(), "Ppap",
            job.Id, submission.Id, submission.Version, submission.OwnerUserId, submission.PartName, submission.PartNumber,
            job.ErrorCode ?? "PROCESSING_FAILED", job.ErrorMessage ?? "PPAP processing failed."), cancellationToken).ConfigureAwait(false);
    }

    private async Task NotifyProgressAsync(IPpapProgressNotifier notifier, PpapJob job, string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await notifier.NotifyAsync(job, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "PPAP progress notification failed for job {JobId}, submission {SubmissionId}, tenant {TenantId}, status {Status}, stage {CurrentStage}, and progress {ProgressPercent}", job.Id, job.SubmissionId, tenantId, job.Status, job.CurrentStage, job.ProgressPercent);
        }
    }

    private static int ProgressFor(PpapJobStage stage) => stage switch
    {
        PpapJobStage.PreprocessingExtraction => 20,
        PpapJobStage.DocumentClassification => 40,
        PpapJobStage.CrossValidation => 60,
        PpapJobStage.ReportGeneration => 80,
        PpapJobStage.NotificationDelivery => 95,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null)
    };
}
