using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Domain;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Domain;

public sealed class PpapJobTests
{
    [Fact]
    public void CreateQueued_Should_InitializeDurableState_When_InputIsValid()
    {
        Guid submissionId = Guid.NewGuid();
        Guid ownerUserId = Guid.NewGuid();

        PpapJob job = PpapJob.CreateQueued(" tenant-1 ", submissionId, 3, ownerUserId);

        job.Id.ShouldNotBe(Guid.Empty);
        job.TenantId.ShouldBe("tenant-1");
        job.SubmissionId.ShouldBe(submissionId);
        job.SubmissionVersion.ShouldBe(3);
        job.OwnerUserId.ShouldBe(ownerUserId);
        job.Status.ShouldBe(PpapJobStatus.Queued);
        job.CurrentStage.ShouldBe(PpapJobStage.UploadVerification);
        job.ProgressPercent.ShouldBe(0);
        job.CreatedAtUtc.ShouldNotBe(default);
    }

    [Fact]
    public void Lifecycle_Should_Complete_When_TransitionsAreValid()
    {
        PpapJob job = CreateJob();

        job.Start();
        job.RenewProcessingLease(Guid.CreateVersion7(), DateTime.UtcNow.AddMinutes(30));
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 10);
        job.AdvanceStage(PpapJobStage.DocumentClassification, 30);
        job.AdvanceStage(PpapJobStage.CrossValidation, 50);
        job.AdvanceStage(PpapJobStage.ReportGeneration, 75);
        job.AdvanceStage(PpapJobStage.NotificationDelivery, 95);
        job.Complete();

        job.Status.ShouldBe(PpapJobStatus.Completed);
        job.ProgressPercent.ShouldBe(100);
        job.StartedAtUtc.ShouldNotBeNull();
        job.CompletedAtUtc.ShouldNotBeNull();
        job.ProcessingLeaseId.ShouldBeNull();
        job.ProcessingLeaseExpiresAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Start_Should_ThrowConflict_When_JobIsAlreadyProcessing()
    {
        PpapJob job = CreateJob();
        job.Start();

        CustomException exception = Should.Throw<CustomException>(job.Start);

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void RenewProcessingLease_Should_RecordLease_When_JobIsProcessing()
    {
        PpapJob job = CreateJob();
        job.Start();
        Guid leaseId = Guid.CreateVersion7();
        DateTime expiresAtUtc = DateTime.UtcNow.AddMinutes(30);

        job.RenewProcessingLease(leaseId, expiresAtUtc);

        job.ProcessingLeaseId.ShouldBe(leaseId);
        job.ProcessingLeaseExpiresAtUtc.ShouldBe(expiresAtUtc);
    }

    [Fact]
    public void RenewProcessingLease_Should_ThrowConflict_When_LeaseIsOwnedByAnotherWorker()
    {
        PpapJob job = CreateJob();
        job.Start();
        job.RenewProcessingLease(Guid.CreateVersion7(), DateTime.UtcNow.AddMinutes(30));

        CustomException exception = Should.Throw<CustomException>(() =>
            job.RenewProcessingLease(Guid.CreateVersion7(), DateTime.UtcNow.AddMinutes(30)));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void AdvanceStage_Should_ThrowConflict_When_ProgressIsNotMonotonic()
    {
        PpapJob job = CreateJob();
        job.Start();
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 20);

        CustomException exception = Should.Throw<CustomException>(() =>
            job.AdvanceStage(PpapJobStage.DocumentClassification, 20));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void AdvanceStage_Should_ThrowConflict_When_StageIsSkipped()
    {
        PpapJob job = CreateJob();
        job.Start();

        CustomException exception = Should.Throw<CustomException>(() =>
            job.AdvanceStage(PpapJobStage.DocumentClassification, 20));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Complete_Should_ThrowConflict_When_FinalStageHasNotBeenReached()
    {
        PpapJob job = CreateJob();
        job.Start();

        CustomException exception = Should.Throw<CustomException>(job.Complete);

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void Fail_Should_RecordSanitizedError_When_JobIsProcessing()
    {
        PpapJob job = CreateJob();
        job.Start();
        job.RenewProcessingLease(Guid.CreateVersion7(), DateTime.UtcNow.AddMinutes(30));

        job.Fail(" PROCESS/FAILED ", " Internal\r\n failure ");

        job.Status.ShouldBe(PpapJobStatus.Failed);
        job.ErrorCode.ShouldBe("PROCESSFAILED");
        job.ErrorMessage.ShouldBe("Internal failure");
        job.FailedAtUtc.ShouldNotBeNull();
        job.ProcessingLeaseId.ShouldBeNull();
        job.ProcessingLeaseExpiresAtUtc.ShouldBeNull();
    }

    [Fact]
    public void Fail_Should_ThrowConflict_When_JobIsQueued()
    {
        PpapJob job = CreateJob();

        CustomException exception = Should.Throw<CustomException>(() => job.Fail("FAILED", "Failure"));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void RuleEvaluationMarkers_Should_BeIdempotent_When_RunIsRedelivered()
    {
        PpapJob job = CreateJob();
        job.Start();
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 20);
        job.AdvanceStage(PpapJobStage.DocumentClassification, 40);

        job.MarkRuleEvaluationQueued().ShouldBeTrue();
        DateTime queuedAtUtc = job.RuleEvaluationQueuedAtUtc!.Value;
        job.MarkRuleEvaluationQueued().ShouldBeFalse();
        job.RuleEvaluationQueuedAtUtc.ShouldBe(queuedAtUtc);
        job.MarkRuleEvaluationStarted().ShouldBeTrue();
        job.MarkRuleEvaluationStarted().ShouldBeFalse();
        Guid runId = Guid.CreateVersion7();
        job.CompleteRuleEvaluation(runId).ShouldBeTrue();
        job.CompleteRuleEvaluation(runId).ShouldBeFalse();

        job.RuleEvaluationRunId.ShouldBe(runId);
        job.RuleEvaluationCompletedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void ReportGenerationMarkers_Should_BeOrderedAndIdempotent()
    {
        PpapJob job = CreateJob();
        job.Start();
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 20);
        job.AdvanceStage(PpapJobStage.DocumentClassification, 40);
        job.MarkRuleEvaluationQueued();
        job.MarkRuleEvaluationStarted();
        job.CompleteRuleEvaluation(Guid.NewGuid());
        job.AdvanceStage(PpapJobStage.CrossValidation, 60);

        job.MarkReportGenerationQueued().ShouldBeTrue();
        DateTime queuedAtUtc = job.ReportGenerationQueuedAtUtc!.Value;
        job.MarkReportGenerationQueued().ShouldBeFalse();
        job.ReportGenerationQueuedAtUtc.ShouldBe(queuedAtUtc);
        job.MarkReportGenerationStarted().ShouldBeTrue();
        job.MarkReportGenerationStarted().ShouldBeFalse();
        job.AdvanceStage(PpapJobStage.ReportGeneration, 80);
        Guid reportId = Guid.NewGuid();
        job.MarkReportGenerationCompleted(reportId).ShouldBeTrue();
        job.MarkReportGenerationCompleted(reportId).ShouldBeFalse();

        job.ReportGenerationReportId.ShouldBe(reportId);
        job.ReportGenerationCompletedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void MarkCompletionEventPublished_Should_BeIdempotent()
    {
        PpapJob job = CreateJob();
        job.Start();
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 10);
        job.AdvanceStage(PpapJobStage.DocumentClassification, 30);
        job.AdvanceStage(PpapJobStage.CrossValidation, 50);
        job.AdvanceStage(PpapJobStage.ReportGeneration, 75);
        job.AdvanceStage(PpapJobStage.NotificationDelivery, 95);
        job.Complete();
        Guid eventId = Guid.NewGuid();

        job.MarkCompletionEventPublished(eventId).ShouldBeTrue();
        job.MarkCompletionEventPublished(Guid.NewGuid()).ShouldBeFalse();
        job.CompletionEventId.ShouldBe(eventId);
        job.CompletionEventPublishedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public void MarkFailureEventPublished_Should_BeIdempotent()
    {
        PpapJob job = CreateJob();
        job.Start();
        job.Fail("FAILED", "Failure");
        Guid eventId = Guid.NewGuid();

        job.MarkFailureEventPublished(eventId).ShouldBeTrue();
        job.MarkFailureEventPublished(Guid.NewGuid()).ShouldBeFalse();
        job.FailureEventId.ShouldBe(eventId);
        job.FailureEventPublishedAtUtc.ShouldNotBeNull();
    }

    private static PpapJob CreateJob() =>
        PpapJob.CreateQueued("tenant-1", Guid.NewGuid(), 1, Guid.NewGuid());
}
