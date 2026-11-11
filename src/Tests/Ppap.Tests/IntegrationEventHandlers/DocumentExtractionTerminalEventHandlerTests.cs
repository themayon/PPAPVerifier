using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Framework.Eventing.Abstractions;
using DES.Framework.Jobs.Services;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Events;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.IntegrationEventHandlers;
using DES.Modules.Ppap.Processing;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ppap.Tests.IntegrationEventHandlers;

public sealed class DocumentExtractionTerminalEventHandlerTests
{
    private const string TenantId = "tenant-1";

    [Fact]
    public async Task CompletedHandler_Should_AdvanceToDocumentClassification_When_AllRequestedDocumentsSucceed()
    {
        await using TestContext test = await CreateTestContextAsync(2);
        PpapSubmissionDocument first = test.Submission.Documents.First();
        PpapSubmissionDocument second = test.Submission.Documents.Last();
        DocumentExtractionCompletedIntegrationEventHandler handler = new(test.DbContext, test.Outbox, test.JobService, Substitute.For<IComplexValidationRequestScheduler>());

        await handler.HandleAsync(Completed(first));
        test.Job.CurrentStage.ShouldBe(PpapJobStage.PreprocessingExtraction);

        DocumentExtractionCompletedIntegrationEvent terminalEvent = Completed(second);
        await handler.HandleAsync(terminalEvent);
        DateTime queuedAtUtc = test.Job.RuleEvaluationQueuedAtUtc!.Value;
        await handler.HandleAsync(terminalEvent with { Id = Guid.CreateVersion7() });

        test.Job.Status.ShouldBe(PpapJobStatus.Processing);
        test.Job.CurrentStage.ShouldBe(PpapJobStage.DocumentClassification);
        test.Job.ProgressPercent.ShouldBe(40);
        test.Job.RuleEvaluationQueuedAtUtc.ShouldBe(queuedAtUtc);
        test.JobService.Received(2).Enqueue<PpapRuleEvaluationJob>(
            Arg.Any<System.Linq.Expressions.Expression<Func<PpapRuleEvaluationJob, Task>>>());
        await test.Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
    }

    [Fact]
    public async Task FailedHandler_Should_FailAndPublishOnceWithoutRefund_When_OutcomeIsInvalidDocument()
    {
        await using TestContext test = await CreateTestContextAsync(1);
        PpapSubmissionDocument document = test.Submission.Documents.Single();
        DocumentExtractionFailedIntegrationEventHandler handler = new(test.DbContext, test.Outbox, test.JobService);
        DocumentExtractionFailedIntegrationEvent failure = Failed(document, "InvalidDocument", " Coversheet/Document ");

        await handler.HandleAsync(failure);
        await handler.HandleAsync(failure with { Id = Guid.CreateVersion7() });

        test.Job.Status.ShouldBe(PpapJobStatus.Failed);
        document.ExtractionTerminalStatus.ShouldBe(DocumentExtractionTerminalStatus.InvalidDocument);
        document.ExtractionFailureCode.ShouldBe("CoversheetDocument");
        test.Account.AvailableCredits.ShouldBe(10);
        test.Account.Transactions.Count(x => x.Kind == CreditTransactionKind.Refund).ShouldBe(0);
        await test.Outbox.Received(1).AddAsync(
            Arg.Is<IIntegrationEvent>(x => x is PpapProcessingFailedIntegrationEvent),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedHandler_Should_NotRefund_When_WrongSourceTypeIsReportedAsFailed()
    {
        await using TestContext test = await CreateTestContextAsync(1);
        PpapSubmissionDocument document = test.Submission.Documents.Single();
        DocumentExtractionFailedIntegrationEventHandler handler = new(test.DbContext, test.Outbox, test.JobService);

        await handler.HandleAsync(Failed(document, "Failed", "InvalidSourceType"));

        test.Job.Status.ShouldBe(PpapJobStatus.Failed);
        test.Account.AvailableCredits.ShouldBe(10);
        test.Account.Transactions.Count(x => x.Kind == CreditTransactionKind.Refund).ShouldBe(0);
    }

    [Fact]
    public async Task FailedHandler_Should_RefundCommittedCredits_When_AnyOutcomeIsSystemFailed()
    {
        await using TestContext test = await CreateTestContextAsync(2);
        PpapSubmissionDocument first = test.Submission.Documents.First();
        PpapSubmissionDocument second = test.Submission.Documents.Last();
        DocumentExtractionFailedIntegrationEventHandler failedHandler = new(test.DbContext, test.Outbox, test.JobService);
        DocumentExtractionCompletedIntegrationEventHandler completedHandler = new(test.DbContext, test.Outbox, test.JobService, Substitute.For<IComplexValidationRequestScheduler>());

        await failedHandler.HandleAsync(Failed(first, "Failed", "Provider/Unavailable"));
        await completedHandler.HandleAsync(Completed(second));

        test.Job.Status.ShouldBe(PpapJobStatus.Failed);
        test.Account.AvailableCredits.ShouldBe(20);
        test.Account.Transactions.Count(x => x.Kind == CreditTransactionKind.Refund).ShouldBe(1);
        await test.Outbox.Received(1).AddAsync(
            Arg.Is<IIntegrationEvent>(x => x is PpapProcessingFailedIntegrationEvent),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompletedHandler_Should_RejectConflictingTerminalEvent()
    {
        await using TestContext test = await CreateTestContextAsync(2);
        PpapSubmissionDocument document = test.Submission.Documents.First();
        DocumentExtractionCompletedIntegrationEventHandler handler = new(test.DbContext, test.Outbox, test.JobService, Substitute.For<IComplexValidationRequestScheduler>());
        DocumentExtractionCompletedIntegrationEvent completed = Completed(document);
        await handler.HandleAsync(completed);

        CustomException exception = await Should.ThrowAsync<CustomException>(() =>
            handler.HandleAsync(completed with { Id = Guid.CreateVersion7(), CanonicalDocumentId = Guid.CreateVersion7() }));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task FailedHandler_Should_RejectEvent_When_OwnerDoesNotCorrelate()
    {
        await using TestContext test = await CreateTestContextAsync(1);
        PpapSubmissionDocument document = test.Submission.Documents.Single();
        DocumentExtractionFailedIntegrationEventHandler handler = new(test.DbContext, test.Outbox, test.JobService);

        CustomException exception = await Should.ThrowAsync<CustomException>(() =>
            handler.HandleAsync(Failed(document, "Failed", "ProviderUnavailable") with { OwnerUserId = Guid.NewGuid() }));

        exception.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        document.ExtractionTerminalStatus.ShouldBeNull();
    }

    [Fact]
    public async Task CompletedHandler_Should_IgnoreEvent_When_SourceFileNoLongerMatchesDocument()
    {
        await using TestContext test = await CreateTestContextAsync(1);
        PpapSubmissionDocument document = test.Submission.Documents.Single();
        DocumentExtractionCompletedIntegrationEventHandler handler = new(test.DbContext, test.Outbox, test.JobService, Substitute.For<IComplexValidationRequestScheduler>());

        await handler.HandleAsync(Completed(document) with { SourceFileAssetId = Guid.CreateVersion7() });

        document.ExtractionTerminalStatus.ShouldBeNull();
        test.Job.CurrentStage.ShouldBe(PpapJobStage.PreprocessingExtraction);
        test.JobService.DidNotReceiveWithAnyArgs().Enqueue<PpapRuleEvaluationJob>(default!);
    }

    [Fact]
    public async Task FailedHandler_Should_IgnoreEvent_When_SourceFileNoLongerMatchesDocument()
    {
        await using TestContext test = await CreateTestContextAsync(1);
        PpapSubmissionDocument document = test.Submission.Documents.Single();
        DocumentExtractionFailedIntegrationEventHandler handler = new(test.DbContext, test.Outbox, test.JobService);

        await handler.HandleAsync(Failed(document, "Failed", "ProviderUnavailable") with { SourceFileAssetId = Guid.CreateVersion7() });

        document.ExtractionTerminalStatus.ShouldBeNull();
        test.Job.Status.ShouldBe(PpapJobStatus.Processing);
    }

    [Fact]
    public async Task CompletedHandler_Should_ProcessEvent_When_SourceFileMatchesDocument()
    {
        await using TestContext test = await CreateTestContextAsync(1);
        PpapSubmissionDocument document = test.Submission.Documents.Single();
        DocumentExtractionCompletedIntegrationEventHandler handler = new(test.DbContext, test.Outbox, test.JobService, Substitute.For<IComplexValidationRequestScheduler>());

        await handler.HandleAsync(Completed(document) with { SourceFileAssetId = document.FileAssetId });

        document.ExtractionTerminalStatus.ShouldBe(DocumentExtractionTerminalStatus.Succeeded);
    }

    private static DocumentExtractionCompletedIntegrationEvent Completed(PpapSubmissionDocument document) => new(
        Guid.CreateVersion7(),
        DateTime.UtcNow,
        TenantId,
        Guid.NewGuid().ToString(),
        "DocumentIntelligence",
        Guid.CreateVersion7(),
        document.Id,
        TestContext.OwnerUserId,
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        document.DocumentCode,
        "provider",
        "model");

    private static DocumentExtractionFailedIntegrationEvent Failed(
        PpapSubmissionDocument document,
        string outcome,
        string errorCode) => new(
            Guid.CreateVersion7(),
            DateTime.UtcNow,
            TenantId,
            Guid.NewGuid().ToString(),
            "DocumentIntelligence",
            Guid.CreateVersion7(),
            document.Id,
            TestContext.OwnerUserId,
            document.DocumentCode,
            "provider",
            "model",
            outcome,
            errorCode);

    private static async Task<TestContext> CreateTestContextAsync(int documentCount)
    {
        SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<PpapDbContext> options = new DbContextOptionsBuilder<PpapDbContext>()
            .UseSqlite(connection)
            .Options;
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        PpapDbContext dbContext = new(
            new TestTenantAccessor(),
            options,
            Options.Create(new DatabaseOptions()),
            environment);
        await dbContext.Database.EnsureCreatedAsync();

        PpapSubmission submission = PpapSubmission.CreateDraft(
            TenantId,
            TestContext.OwnerUserId,
            "Part",
            "PN-100",
            "A",
            3);
        for (int index = 0; index < documentCount; index++)
        {
            string documentCode = index == 0 ? "PSW" : "DFMEA";
            PpapSubmissionDocument document = submission.AddDocumentSlot(documentCode);
            submission.ApplyFinalizedFile(document.Id, Guid.CreateVersion7(), true);
            document.MarkExtractionRequestPublished(Guid.CreateVersion7(), DateTime.UtcNow);
        }
        int reservationVersion = submission.Version;
        submission.Submit();

        PpapJob job = PpapJob.CreateQueued(TenantId, submission.Id, submission.Version, submission.OwnerUserId);
        job.Start();
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 20);

        CreditAccount account = CreditAccount.Create(TenantId, submission.OwnerUserId);
        account.TopUp(20, "initial-topup");
        string operationReference = $"submission:{submission.Id:N}:v{reservationVersion}";
        account.Reserve(10, operationReference);
        account.CommitReservation(10, operationReference);

        dbContext.AddRange(submission, job, account);
        await dbContext.SaveChangesAsync();
        IOutboxWriter outbox = Substitute.For<IOutboxWriter>();
        IJobService jobService = Substitute.For<IJobService>();
        return new TestContext(connection, dbContext, outbox, jobService, submission, job, account);
    }

    private sealed class TestTenantAccessor : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(new AppTenantInfo(TenantId, TenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }

    private sealed class TestContext(
        SqliteConnection connection,
        PpapDbContext dbContext,
        IOutboxWriter outbox,
        IJobService jobService,
        PpapSubmission submission,
        PpapJob job,
        CreditAccount account) : IAsyncDisposable
    {
        internal static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        internal PpapDbContext DbContext { get; } = dbContext;
        internal IOutboxWriter Outbox { get; } = outbox;
        internal IJobService JobService { get; } = jobService;
        internal PpapSubmission Submission { get; } = submission;
        internal PpapJob Job { get; } = job;
        internal CreditAccount Account { get; } = account;

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
