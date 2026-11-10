using DES.Framework.Eventing.Abstractions;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Contracts.Dtos;
using DES.Modules.DocumentIntelligence.Contracts.Services;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Processing;
using DES.Modules.Rules.Contracts;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ppap.Tests.Processing;

public sealed class ComplexValidationRequestSchedulerTests
{
    private const string TenantId = "tenant-1";
    private static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ScheduleReadyAsync_Should_PublishOnce_When_AllDependenciesAreReady()
    {
        await using TestContext test = await CreateTestContextAsync();
        Guid ruleVersionId = Guid.CreateVersion7();
        var plan = new ComplexValidationPlanItem(
            ruleVersionId,
            "DIMR-DWG",
            "DIMR to DWG",
            RuleCategory.CrossDocument,
            "json-path-compare:v1",
            """{"executorKey":"json-path-compare:v1","leftDocumentCode":"DIMR","rightDocumentCode":"DWG","leftPath":"value","rightPath":"value","operator":"Equal"}""",
            ["DIMR", "DWG"]);
        test.PlanReader.ReadAsync(test.Submission.RuleTemplateVersionId!.Value, Arg.Any<CancellationToken>())
            .Returns([plan]);
        test.CanonicalReader.ReadAsync(OwnerUserId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(test.Revisions.Select(revision => new CanonicalDocumentDto(
                revision.CanonicalDocumentId!.Value,
                revision.ExtractionAttemptId!.Value,
                "v1",
                "{\"value\":1}",
                null,
                revision.GeneratedWorkbookFileAssetId,
                DateTime.UtcNow)).ToArray());
        test.ProvenanceReader.ReadAsync(OwnerUserId, Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(test.Revisions.Select(revision => new ExtractionProvenanceDto(
                revision.ExtractionAttemptId!.Value,
                revision.SubmissionDocumentId,
                revision.SourceFileAssetId!.Value,
                test.Submission.Documents.Single(document => document.Id == revision.SubmissionDocumentId).DocumentCode,
                $"sha-{revision.RevisionNumber}",
                "provider",
                "model",
                "v1",
                "Succeeded",
                revision.CanonicalDocumentId!.Value,
                "v1",
                null,
                revision.GeneratedWorkbookFileAssetId,
                DateTime.UtcNow.AddMinutes(-1),
                DateTime.UtcNow)).ToArray());
        var sut = new ComplexValidationRequestScheduler(
            test.Db,
            test.PlanReader,
            test.CanonicalReader,
            test.ProvenanceReader,
            test.Outbox,
            Options.Create(new ComplexValidationOptions { Enabled = true }));
        DateTime occurredOnUtc = DateTime.UtcNow;

        await sut.ScheduleReadyAsync(test.Submission, "correlation", occurredOnUtc);
        await sut.ScheduleReadyAsync(test.Submission, "correlation", occurredOnUtc);

        PpapComplexValidationRequest request = await test.Db.PpapComplexValidationRequests.SingleAsync();
        request.RuleVersionId.ShouldBe(ruleVersionId);
        request.InputFingerprint.Length.ShouldBe(64);
        await test.Outbox.Received(1).AddAsync(
            Arg.Is<IIntegrationEvent>(value => IsExpectedRequest(value, test.Submission.Id, request)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScheduleReadyAsync_Should_Wait_When_DependencyIsMissing()
    {
        await using TestContext test = await CreateTestContextAsync(includeDrawing: false);
        var plan = new ComplexValidationPlanItem(
            Guid.CreateVersion7(),
            "DIMR-DWG",
            "DIMR to DWG",
            RuleCategory.CrossDocument,
            "json-path-compare:v1",
            "{}",
            ["DIMR", "DWG"]);
        test.PlanReader.ReadAsync(test.Submission.RuleTemplateVersionId!.Value, Arg.Any<CancellationToken>())
            .Returns([plan]);
        var sut = new ComplexValidationRequestScheduler(
            test.Db,
            test.PlanReader,
            test.CanonicalReader,
            test.ProvenanceReader,
            test.Outbox,
            Options.Create(new ComplexValidationOptions { Enabled = true }));

        await sut.ScheduleReadyAsync(test.Submission, "correlation", DateTime.UtcNow);

        (await test.Db.PpapComplexValidationRequests.CountAsync()).ShouldBe(0);
        await test.Outbox.DidNotReceiveWithAnyArgs().AddAsync(default!, default);
        await test.CanonicalReader.DidNotReceiveWithAnyArgs().ReadAsync(Guid.Empty, default!, default);
    }

    private static bool IsExpectedRequest(
        IIntegrationEvent value,
        Guid submissionId,
        PpapComplexValidationRequest request)
    {
        ComplexValidationRequestedIntegrationEvent? requested = value as ComplexValidationRequestedIntegrationEvent;
        return requested is not null
            && requested.SubmissionId == submissionId
            && requested.ValidationRunId == request.ValidationRunId
            && requested.InputFingerprint == request.InputFingerprint
            && requested.Documents.Count == 2;
    }

    private static async Task<TestContext> CreateTestContextAsync(bool includeDrawing = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        DbContextOptions<PpapDbContext> options = new DbContextOptionsBuilder<PpapDbContext>()
            .UseSqlite(connection)
            .Options;
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        var db = new PpapDbContext(
            new TestTenantAccessor(),
            options,
            Options.Create(new DatabaseOptions()),
            environment);
        await db.Database.EnsureCreatedAsync();
        PpapSubmission submission = PpapSubmission.CreateDraft(
            TenantId,
            OwnerUserId,
            "Part",
            "PN-100",
            "A",
            3,
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        var revisions = new List<PpapDocumentRevision>
        {
            CreateReadyRevision(submission.AddDocumentSlot("DIMR"), 1)
        };
        if (includeDrawing) revisions.Add(CreateReadyRevision(submission.AddDocumentSlot("DWG"), 1));
        db.Add(submission);
        db.PpapDocumentRevisions.AddRange(revisions);
        await db.SaveChangesAsync();
        return new TestContext(
            connection,
            db,
            submission,
            revisions,
            Substitute.For<IComplexValidationPlanReader>(),
            Substitute.For<ICanonicalDocumentReader>(),
            Substitute.For<IExtractionProvenanceReader>(),
            Substitute.For<IOutboxWriter>());
    }

    private static PpapDocumentRevision CreateReadyRevision(PpapSubmissionDocument document, int revisionNumber)
    {
        PpapDocumentRevision revision = PpapDocumentRevision.Create(
            TenantId,
            document.Id,
            revisionNumber,
            OwnerUserId,
            $"{document.DocumentCode}.pdf",
            "application/pdf",
            100);
        DateTime now = DateTime.UtcNow;
        revision.ApplyFinalizedFile(Guid.CreateVersion7(), "application/pdf", 100, true, now);
        revision.QueueExtraction(Guid.CreateVersion7(), now);
        revision.RecordExtractionCompleted(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            Guid.CreateVersion7(),
            "provider",
            "v1",
            now);
        return revision;
    }

    private sealed class TestTenantAccessor : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(new AppTenantInfo(TenantId, TenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }

    private sealed class TestContext(
        SqliteConnection connection,
        PpapDbContext db,
        PpapSubmission submission,
        IReadOnlyList<PpapDocumentRevision> revisions,
        IComplexValidationPlanReader planReader,
        ICanonicalDocumentReader canonicalReader,
        IExtractionProvenanceReader provenanceReader,
        IOutboxWriter outbox) : IAsyncDisposable
    {
        internal PpapDbContext Db { get; } = db;
        internal PpapSubmission Submission { get; } = submission;
        internal IReadOnlyList<PpapDocumentRevision> Revisions { get; } = revisions;
        internal IComplexValidationPlanReader PlanReader { get; } = planReader;
        internal ICanonicalDocumentReader CanonicalReader { get; } = canonicalReader;
        internal IExtractionProvenanceReader ProvenanceReader { get; } = provenanceReader;
        internal IOutboxWriter Outbox { get; } = outbox;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
