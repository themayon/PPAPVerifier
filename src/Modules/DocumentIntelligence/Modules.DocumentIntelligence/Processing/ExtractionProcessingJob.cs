using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using DES.Framework.Core.Exceptions;
using DES.Framework.Eventing.Abstractions;
using DES.Framework.Jobs.Services;
using DES.Framework.Shared.Multitenancy;
using DES.Modules.DocumentIntelligence.Contracts.Events;
using DES.Modules.DocumentIntelligence.Data;
using DES.Modules.DocumentIntelligence.Domain;
using DES.Modules.DocumentIntelligence.Providers;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DES.Modules.DocumentIntelligence.Processing;

public sealed class ExtractionProcessingJob(
    IMultiTenantStore<AppTenantInfo> tenantStore,
    IMultiTenantContextSetter tenantContextSetter,
    IServiceScopeFactory scopeFactory,
    IOptions<AspectPdfExtractionOptions> options,
    IOptions<DiagramDrawingExtractionOptions> drawingOptions,
    ILogger<ExtractionProcessingJob> logger)
{
    private readonly TimeSpan _leaseDuration = TimeSpan.FromMinutes(Math.Max(options.Value.LeaseMinutes, drawingOptions.Value.LeaseMinutes));
    private readonly int _maxAttempts = options.Value.MaxAttempts;

    public async Task RunAsync(string tenantId, Guid attemptId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (attemptId == Guid.Empty)
        {
            throw new ArgumentException("Extraction attempt ID is required.", nameof(attemptId));
        }

        AppTenantInfo tenant = await tenantStore.GetAsync(tenantId).ConfigureAwait(false)
            ?? throw new NotFoundException($"Tenant {tenantId} not found for extraction attempt {attemptId}.");
        tenantContextSetter.MultiTenantContext = new MultiTenantContext<AppTenantInfo>(tenant);

        using IServiceScope scope = scopeFactory.CreateScope();
        DocumentIntelligenceDbContext dbContext = scope.ServiceProvider.GetRequiredService<DocumentIntelligenceDbContext>();
        IDocumentExtractionProvider provider = scope.ServiceProvider.GetRequiredService<IDocumentExtractionProvider>();
        IOutboxWriter outbox = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();
        IJobService jobService = scope.ServiceProvider.GetRequiredService<IJobService>();

        DateTime now = DateTime.UtcNow;
        string claimedBy = BuildClaimedBy();
        DateTime claimedUntilUtc = now.Add(_leaseDuration);
        int claimed = await dbContext.ExtractionAttempts
            .Where(x => x.Id == attemptId
                && x.TenantId == tenantId
                && x.AttemptCount < _maxAttempts
                && (x.Status == ExtractionAttemptStatus.Pending
                    || (x.Status == ExtractionAttemptStatus.Claimed
                        && (x.ClaimedUntilUtc == null || x.ClaimedUntilUtc <= now))))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.Status, ExtractionAttemptStatus.Claimed)
                .SetProperty(x => x.ClaimedAtUtc, now)
                .SetProperty(x => x.ClaimedBy, claimedBy)
                .SetProperty(x => x.ClaimedUntilUtc, claimedUntilUtc)
                .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                .SetProperty(x => x.UpdatedAtUtc, now)
                .SetProperty(x => x.Version, x => x.Version + 1),
                cancellationToken)
            .ConfigureAwait(false);

        dbContext.ChangeTracker.Clear();
        ExtractionAttempt attempt = await dbContext.ExtractionAttempts
            .FirstOrDefaultAsync(x => x.Id == attemptId && x.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Extraction attempt {attemptId} not found.");

        if (claimed == 0)
        {
            await HandleUnclaimedAsync(dbContext, outbox, attempt, now, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            attempt.RenewLease(claimedBy, DateTime.UtcNow.Add(_leaseDuration));
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            ExtractionProviderResult result = await provider.ExtractAsync(
                new ExtractionProviderRequest(
                    attempt.Id,
                    attempt.OwnerUserId,
                    attempt.SourceFileAssetId,
                    attempt.DocumentCode,
                    attempt.Model),
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(result.ProviderRequestId))
            {
                attempt.MarkSubmitted(result.ProviderRequestId);
            }

            switch (result.Outcome)
            {
                case ExtractionProviderOutcome.Succeeded:
                    await CompleteSucceededAsync(dbContext, outbox, attempt, result, cancellationToken).ConfigureAwait(false);
                    break;
                case ExtractionProviderOutcome.InvalidDocument:
                    await CompleteInvalidAsync(dbContext, outbox, attempt, result, cancellationToken).ConfigureAwait(false);
                    break;
                case ExtractionProviderOutcome.Failed:
                    await HandleProviderFailureAsync(dbContext, outbox, jobService, attempt, result, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new InvalidOperationException("The extraction provider returned an unknown outcome.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Document extraction failed unexpectedly for attempt {AttemptId} and tenant {TenantId}", attemptId, tenantId);
            bool handled = await TryHandleUnexpectedAsync(
                dbContext,
                outbox,
                jobService,
                attemptId,
                tenantId,
                claimedBy).ConfigureAwait(false);
            if (!handled)
            {
                throw;
            }
        }
    }

    private async Task HandleUnclaimedAsync(
        DocumentIntelligenceDbContext dbContext,
        IOutboxWriter outbox,
        ExtractionAttempt attempt,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (attempt.Status == ExtractionAttemptStatus.Succeeded)
        {
            CanonicalDocument document = await dbContext.CanonicalDocuments
                .FirstOrDefaultAsync(x => x.ExtractionAttemptId == attempt.Id, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new NotFoundException($"Canonical document for extraction attempt {attempt.Id} not found.");
            await PublishCompletionAsync(dbContext, outbox, attempt, document, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (attempt.Status is ExtractionAttemptStatus.InvalidDocument or ExtractionAttemptStatus.DeadLettered)
        {
            await PublishFailureAsync(dbContext, outbox, attempt, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (attempt.Status == ExtractionAttemptStatus.Claimed
            && attempt.ClaimedUntilUtc is not null
            && attempt.ClaimedUntilUtc > now)
        {
            return;
        }
        if (attempt.AttemptCount >= _maxAttempts
            && attempt.Status is ExtractionAttemptStatus.Pending or ExtractionAttemptStatus.Claimed)
        {
            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            attempt.DeadLetterExhausted("MaxAttemptsExceeded", "Document extraction exhausted its configured attempts.");
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await AddFailureEventAsync(outbox, attempt, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task CompleteSucceededAsync(
        DocumentIntelligenceDbContext dbContext,
        IOutboxWriter outbox,
        ExtractionAttempt attempt,
        ExtractionProviderResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(result.SchemaVersion) || string.IsNullOrWhiteSpace(result.CanonicalJson))
        {
            throw new InvalidDataException("The provider success result did not include canonical document data.");
        }

        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        CanonicalDocument? document = await dbContext.CanonicalDocuments
            .FirstOrDefaultAsync(x => x.ExtractionAttemptId == attempt.Id, cancellationToken)
            .ConfigureAwait(false);
        if (document is null)
        {
            document = CanonicalDocument.Create(
                attempt.TenantId,
                attempt.Id,
                attempt.OwnerUserId,
                result.SchemaVersion,
                result.CanonicalJson,
                result.RawResponseFileAssetId,
                result.GeneratedExcelFileAssetId);
            dbContext.CanonicalDocuments.Add(document);
        }
        attempt.Succeed();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AddCompletionEventAsync(outbox, attempt, document, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task CompleteInvalidAsync(
        DocumentIntelligenceDbContext dbContext,
        IOutboxWriter outbox,
        ExtractionAttempt attempt,
        ExtractionProviderResult result,
        CancellationToken cancellationToken)
    {
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        attempt.MarkInvalidDocument(
            result.ErrorCode ?? "InvalidDocument",
            result.ErrorMessage ?? "The provider determined that the document is invalid.");
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await AddFailureEventAsync(outbox, attempt, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleProviderFailureAsync(
        DocumentIntelligenceDbContext dbContext,
        IOutboxWriter outbox,
        IJobService jobService,
        ExtractionAttempt attempt,
        ExtractionProviderResult result,
        CancellationToken cancellationToken)
    {
        attempt.Fail(
            result.ErrorCode ?? "ProviderFailed",
            result.ErrorMessage ?? "The extraction provider failed.");
        if (attempt.AttemptCount >= _maxAttempts)
        {
            await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            attempt.DeadLetter(attempt.ErrorCode!, attempt.ErrorMessage!);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await AddFailureEventAsync(outbox, attempt, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        attempt.Requeue();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        jobService.Enqueue<ExtractionProcessingJob>(
            job => job.RunAsync(attempt.TenantId, attempt.Id, CancellationToken.None));
    }

    [SuppressMessage("Design", "CA1031", Justification = "Failure recovery must not replace the original processing exception.")]
    private async Task<bool> TryHandleUnexpectedAsync(
        DocumentIntelligenceDbContext dbContext,
        IOutboxWriter outbox,
        IJobService jobService,
        Guid attemptId,
        string tenantId,
        string claimedBy)
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            ExtractionAttempt? attempt = await dbContext.ExtractionAttempts
                .FirstOrDefaultAsync(x => x.Id == attemptId && x.TenantId == tenantId, CancellationToken.None)
                .ConfigureAwait(false);
            if (attempt is null)
            {
                return false;
            }
            if (attempt.Status is ExtractionAttemptStatus.Succeeded or ExtractionAttemptStatus.InvalidDocument or ExtractionAttemptStatus.DeadLettered)
            {
                await HandleUnclaimedAsync(dbContext, outbox, attempt, DateTime.UtcNow, CancellationToken.None).ConfigureAwait(false);
                return true;
            }
            if (attempt.Status != ExtractionAttemptStatus.Claimed
                || !string.Equals(attempt.ClaimedBy, claimedBy, StringComparison.Ordinal))
            {
                return attempt.Status == ExtractionAttemptStatus.Pending;
            }

            attempt.Fail("ExtractionProcessingError", "Document extraction failed unexpectedly.");
            if (attempt.AttemptCount >= _maxAttempts)
            {
                await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(CancellationToken.None).ConfigureAwait(false);
                attempt.DeadLetter(attempt.ErrorCode!, attempt.ErrorMessage!);
                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                await AddFailureEventAsync(outbox, attempt, CancellationToken.None).ConfigureAwait(false);
                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                attempt.Requeue();
                await dbContext.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                jobService.Enqueue<ExtractionProcessingJob>(
                    job => job.RunAsync(tenantId, attemptId, CancellationToken.None));
            }
            return true;
        }
        catch (Exception failureException)
        {
            logger.LogError(failureException, "Failed to persist extraction failure for attempt {AttemptId} and tenant {TenantId}", attemptId, tenantId);
            return false;
        }
    }

    private static async Task PublishCompletionAsync(
        DocumentIntelligenceDbContext dbContext,
        IOutboxWriter outbox,
        ExtractionAttempt attempt,
        CanonicalDocument document,
        CancellationToken cancellationToken)
    {
        if (attempt.CompletionEventPublishedAtUtc is not null)
        {
            return;
        }
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await AddCompletionEventAsync(outbox, attempt, document, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task PublishFailureAsync(
        DocumentIntelligenceDbContext dbContext,
        IOutboxWriter outbox,
        ExtractionAttempt attempt,
        CancellationToken cancellationToken)
    {
        if (attempt.FailureEventPublishedAtUtc is not null)
        {
            return;
        }
        await using IDbContextTransaction transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await AddFailureEventAsync(outbox, attempt, cancellationToken).ConfigureAwait(false);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddCompletionEventAsync(
        IOutboxWriter outbox,
        ExtractionAttempt attempt,
        CanonicalDocument document,
        CancellationToken cancellationToken)
    {
        Guid eventId = attempt.CompletionEventId ?? Guid.CreateVersion7();
        if (!attempt.MarkCompletionEventPublished(eventId))
        {
            return;
        }
        await outbox.AddAsync(new DocumentExtractionCompletedIntegrationEvent(
            eventId,
            DateTime.UtcNow,
            attempt.TenantId,
            Activity.Current?.Id ?? eventId.ToString(),
            "DocumentIntelligence",
            attempt.Id,
            attempt.SubmissionDocumentId,
            attempt.OwnerUserId,
            document.Id,
            document.RawResponseFileAssetId,
            document.GeneratedExcelFileAssetId,
            attempt.DocumentCode,
            attempt.Provider,
            attempt.Model,
            attempt.SourceFileAssetId), cancellationToken).ConfigureAwait(false);
    }

    private static async Task AddFailureEventAsync(
        IOutboxWriter outbox,
        ExtractionAttempt attempt,
        CancellationToken cancellationToken)
    {
        Guid eventId = attempt.FailureEventId ?? Guid.CreateVersion7();
        if (!attempt.MarkFailureEventPublished(eventId))
        {
            return;
        }
        await outbox.AddAsync(new DocumentExtractionFailedIntegrationEvent(
            eventId,
            DateTime.UtcNow,
            attempt.TenantId,
            Activity.Current?.Id ?? eventId.ToString(),
            "DocumentIntelligence",
            attempt.Id,
            attempt.SubmissionDocumentId,
            attempt.OwnerUserId,
            attempt.DocumentCode,
            attempt.Provider,
            attempt.Model,
            attempt.Status == ExtractionAttemptStatus.InvalidDocument ? "InvalidDocument" : "Failed",
            attempt.ErrorCode ?? "ExtractionFailed",
            attempt.SourceFileAssetId), cancellationToken).ConfigureAwait(false);
    }

    private static string BuildClaimedBy()
    {
        string value = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        return value.Length <= ExtractionAttempt.ClaimedByMaxLength
            ? value
            : value[..ExtractionAttempt.ClaimedByMaxLength];
    }
}
