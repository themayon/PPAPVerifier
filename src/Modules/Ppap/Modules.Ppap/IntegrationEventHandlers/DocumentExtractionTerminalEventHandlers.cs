using System.Data;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Framework.Eventing.Abstractions;
using DES.Framework.Jobs.Services;
using DES.Modules.DocumentIntelligence.Contracts.Events;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Processing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace DES.Modules.Ppap.IntegrationEventHandlers;

[SuppressMessage("Naming", "CA1711", Justification = "The required integration-event handler name describes its role.")]
public sealed class DocumentExtractionCompletedIntegrationEventHandler(
    PpapDbContext dbContext,
    IOutboxWriter outbox,
    IJobService jobService,
    IComplexValidationRequestScheduler validationScheduler)
    : IIntegrationEventHandler<DocumentExtractionCompletedIntegrationEvent>
{
    public Task HandleAsync(DocumentExtractionCompletedIntegrationEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return DocumentExtractionTerminalEventProcessor.ProcessCompletedAsync(dbContext, outbox, jobService, validationScheduler, @event, ct);
    }
}

[SuppressMessage("Naming", "CA1711", Justification = "The required integration-event handler name describes its role.")]
public sealed class DocumentExtractionFailedIntegrationEventHandler(
    PpapDbContext dbContext,
    IOutboxWriter outbox,
    IJobService jobService)
    : IIntegrationEventHandler<DocumentExtractionFailedIntegrationEvent>
{
    public Task HandleAsync(DocumentExtractionFailedIntegrationEvent @event, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return DocumentExtractionTerminalEventProcessor.ProcessFailedAsync(dbContext, outbox, jobService, @event, ct);
    }
}

internal static class DocumentExtractionTerminalEventProcessor
{
    private const int SubmissionCreditCost = 10;
    private const int DocumentClassificationProgress = 40;
    private const string ExtractionFailureCode = "DOCUMENT_EXTRACTION_FAILED";

    internal static Task ProcessCompletedAsync(
        PpapDbContext dbContext,
        IOutboxWriter outbox,
        IJobService jobService,
        IComplexValidationRequestScheduler validationScheduler,
        DocumentExtractionCompletedIntegrationEvent @event,
        CancellationToken cancellationToken) => ProcessAsync(
            dbContext,
            outbox,
            jobService,
            validationScheduler,
            @event.TenantId,
            @event.CorrelationId,
            @event.OccurredOnUtc,
            @event.SubmissionDocumentId,
            @event.OwnerUserId,
            document => document.RecordExtractionCompleted(
                @event.TenantId,
                @event.DocumentCode,
                @event.ExtractionAttemptId,
                @event.CanonicalDocumentId,
                @event.RawResponseFileAssetId,
                @event.GeneratedExcelFileAssetId,
                @event.OccurredOnUtc),
            revision => revision.RecordExtractionCompleted(
                @event.ExtractionAttemptId,
                @event.CanonicalDocumentId,
                @event.RawResponseFileAssetId,
                @event.GeneratedExcelFileAssetId,
                @event.Provider,
                @event.Model,
                @event.OccurredOnUtc),
            cancellationToken);

    internal static Task ProcessFailedAsync(
        PpapDbContext dbContext,
        IOutboxWriter outbox,
        IJobService jobService,
        DocumentExtractionFailedIntegrationEvent @event,
        CancellationToken cancellationToken)
    {
        DocumentExtractionTerminalStatus terminalStatus = @event.Outcome switch
        {
            "InvalidDocument" => DocumentExtractionTerminalStatus.InvalidDocument,
            "Failed" => DocumentExtractionTerminalStatus.Failed,
            _ => throw new ArgumentException("Unsupported document extraction outcome.", nameof(@event))
        };

        return ProcessAsync(
            dbContext,
            outbox,
            jobService,
            null,
            @event.TenantId,
            @event.CorrelationId,
            @event.OccurredOnUtc,
            @event.SubmissionDocumentId,
            @event.OwnerUserId,
            document => document.RecordExtractionFailed(
                @event.TenantId,
                @event.DocumentCode,
                @event.ExtractionAttemptId,
                terminalStatus,
                @event.ErrorCode,
                @event.OccurredOnUtc),
            revision => revision.RecordExtractionFailed(
                @event.ExtractionAttemptId,
                @event.ErrorCode,
                @event.OccurredOnUtc),
            cancellationToken);
    }

    private static async Task ProcessAsync(
        PpapDbContext dbContext,
        IOutboxWriter outbox,
        IJobService jobService,
        IComplexValidationRequestScheduler? validationScheduler,
        string tenantId,
        string correlationId,
        DateTime occurredOnUtc,
        Guid submissionDocumentId,
        Guid ownerUserId,
        Func<PpapSubmissionDocument, bool> applyTerminalEvent,
        Func<PpapDocumentRevision, bool> applyRevisionTerminalEvent,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (submissionDocumentId == Guid.Empty)
        {
            throw new ArgumentException("Submission document ID is required.", nameof(submissionDocumentId));
        }
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("Owner user ID is required.", nameof(ownerUserId));
        }

        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);

        PpapSubmission submission = await dbContext.PpapSubmissions
            .Include(x => x.Documents)
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId
                    && x.Documents.Any(document => document.Id == submissionDocumentId),
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Conflict("The extraction event does not correlate to a PPAP submission document in this tenant.");
        if (submission.OwnerUserId != ownerUserId)
        {
            throw Conflict("The extraction event owner does not correlate to the PPAP submission.");
        }

        PpapSubmissionDocument document = submission.Documents.Single(x => x.Id == submissionDocumentId);
        _ = applyTerminalEvent(document);
        PpapDocumentRevision? revision = await dbContext.PpapDocumentRevisions
            .FirstOrDefaultAsync(
                x => x.SubmissionDocumentId == submissionDocumentId && x.IsCurrent,
                cancellationToken)
            .ConfigureAwait(false);
        if (revision is not null) _ = applyRevisionTerminalEvent(revision);
        if (validationScheduler is not null)
        {
            await validationScheduler.ScheduleReadyAsync(submission, correlationId, occurredOnUtc, cancellationToken).ConfigureAwait(false);
        }

        PpapJob? job = await dbContext.PpapJobs
            .FirstOrDefaultAsync(
                x => x.TenantId == tenantId
                    && x.SubmissionId == submission.Id
                    && x.SubmissionVersion == submission.Version
                    && x.OwnerUserId == ownerUserId,
                cancellationToken)
            .ConfigureAwait(false);
        if (job is null)
        {
            if (submission.Status != SubmissionStatus.Draft)
            {
                throw Conflict("The extraction event does not correlate to the PPAP processing job.");
            }
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        PpapSubmissionDocument[] requestedDocuments = submission.Documents
            .Where(x => x.Status == SubmissionDocumentStatus.Available
                && x.ExtractionRequestPublishedAtUtc is not null)
            .ToArray();
        int availableDocumentCount = submission.Documents.Count(x => x.Status == SubmissionDocumentStatus.Available);
        if (requestedDocuments.Length != availableDocumentCount
            || requestedDocuments.Any(x => x.ExtractionTerminalStatus is null))
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        bool anyFailure = requestedDocuments.Any(
            x => x.ExtractionTerminalStatus is DocumentExtractionTerminalStatus.InvalidDocument
                or DocumentExtractionTerminalStatus.Failed);
        if (!anyFailure)
        {
            AdvanceToRulesBoundary(job);
            _ = job.MarkRuleEvaluationQueued();
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            if (job.RuleEvaluationCompletedAtUtc is null)
            {
                jobService.Enqueue<PpapRuleEvaluationJob>(
                    evaluationJob => evaluationJob.RunAsync(tenantId, job.Id, CancellationToken.None));
            }
            return;
        }

        if (job.Status == PpapJobStatus.Processing)
        {
            if (job.CurrentStage != PpapJobStage.PreprocessingExtraction)
            {
                throw Conflict("The PPAP job is not waiting for document extraction results.");
            }
            job.Fail(ExtractionFailureCode, "One or more PPAP documents failed extraction.");
        }
        else if (job.Status != PpapJobStatus.Failed
            || !string.Equals(job.ErrorCode, ExtractionFailureCode, StringComparison.Ordinal))
        {
            throw Conflict("The PPAP job is already in a conflicting terminal state.");
        }

        bool hasSystemFailure = requestedDocuments.Any(IsRefundEligibleSystemFailure);
        if (hasSystemFailure)
        {
            await RefundCommittedCreditsAsync(dbContext, job, cancellationToken).ConfigureAwait(false);
        }

        Guid failureEventId = job.FailureEventId ?? Guid.CreateVersion7();
        if (job.MarkFailureEventPublished(failureEventId))
        {
            await outbox.AddAsync(new PpapProcessingFailedIntegrationEvent(
                failureEventId,
                DateTime.UtcNow,
                tenantId,
                string.IsNullOrWhiteSpace(correlationId)
                    ? Activity.Current?.Id ?? failureEventId.ToString()
                    : correlationId,
                "Ppap",
                job.Id,
                submission.Id,
                submission.Version,
                submission.OwnerUserId,
                submission.PartName,
                submission.PartNumber,
                job.ErrorCode ?? ExtractionFailureCode,
                job.ErrorMessage ?? "PPAP document extraction failed."), cancellationToken).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsRefundEligibleSystemFailure(PpapSubmissionDocument document)
    {
        if (document.ExtractionTerminalStatus != DocumentExtractionTerminalStatus.Failed)
        {
            return false;
        }

        return document.ExtractionFailureCode is not { } failureCode
            || (!failureCode.Equals("InvalidDocument", StringComparison.OrdinalIgnoreCase)
                && !failureCode.Equals("CoversheetDocument", StringComparison.OrdinalIgnoreCase)
                && !failureCode.Equals("InvalidSourceType", StringComparison.OrdinalIgnoreCase)
                && !failureCode.Equals("WrongDocumentType", StringComparison.OrdinalIgnoreCase));
    }

    private static void AdvanceToRulesBoundary(PpapJob job)
    {
        if (job.Status != PpapJobStatus.Processing)
        {
            throw Conflict("The PPAP job is not processing extraction results.");
        }
        if (job.CurrentStage == PpapJobStage.DocumentClassification)
        {
            return;
        }
        if (job.CurrentStage != PpapJobStage.PreprocessingExtraction)
        {
            throw Conflict("The PPAP job is not waiting for document extraction results.");
        }

        job.AdvanceStage(PpapJobStage.DocumentClassification, DocumentClassificationProgress);
    }

    private static async Task RefundCommittedCreditsAsync(
        PpapDbContext dbContext,
        PpapJob job,
        CancellationToken cancellationToken)
    {
        CreditAccount account = await dbContext.CreditAccounts
            .Include(x => x.Transactions)
            .FirstOrDefaultAsync(
                x => x.TenantId == job.TenantId && x.OwnerUserId == job.OwnerUserId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw Conflict("The PPAP credit account could not be found for an eligible extraction refund.");
        string operationReference = $"submission:{job.SubmissionId:N}:v{job.SubmissionVersion - 1}";
        if (account.Transactions.Any(x => x.Kind == CreditTransactionKind.Commit
            && x.Reference == operationReference
            && x.Amount == SubmissionCreditCost))
        {
            account.Refund(SubmissionCreditCost, operationReference);
        }
    }

    private static CustomException Conflict(string message) =>
        new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
