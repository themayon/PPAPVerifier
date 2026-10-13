using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Features.v1.Reports.CreateManualPpapFinding;
using DES.Modules.Ppap.Features.v1.Reports.GetPpapReport;
using DES.Modules.Ppap.Features.v1.Reports.RevokePpapFindingWaiver;
using DES.Modules.Ppap.Features.v1.Reports.WaivePpapFinding;
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

public sealed class PpapFindingReviewHandlerTests
{
    private const string TenantId = "tenant-review";
    private const string OtherTenantId = "tenant-other";
    private static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CreatorUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ApproverUserId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RevokerUserId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public void Create_Should_PreserveDerivedDocumentCodeAndImmutableCorrelations()
    {
        DateTime createdAtUtc = DateTime.UtcNow;
        Guid documentId = Guid.NewGuid();
        Guid canonicalId = Guid.NewGuid();
        Guid attemptId = Guid.NewGuid();

        PpapManualFinding finding = PpapManualFinding.Create(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            OwnerUserId,
            " Message ",
            " Evidence ",
            CreatorUserId,
            createdAtUtc,
            documentId,
            canonicalId,
            attemptId,
            "psw");

        finding.DocumentCode.ShouldBe("PSW");
        finding.SubmissionDocumentId.ShouldBe(documentId);
        finding.CanonicalDocumentId.ShouldBe(canonicalId);
        finding.ExtractionAttemptId.ShouldBe(attemptId);
        finding.CreatedByUserId.ShouldBe(CreatorUserId);
        finding.CreatedAtUtc.ShouldBe(createdAtUtc);
        finding.Message.ShouldBe("Message");
        finding.EvidenceNote.ShouldBe("Evidence");
        Should.Throw<ArgumentException>(() => PpapManualFinding.Create(
            TenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            OwnerUserId,
            "Message",
            null,
            CreatorUserId,
            createdAtUtc,
            documentId,
            canonicalId,
            attemptId));
    }

    [Fact]
    public async Task Handle_Should_CreateOnlyForCompletedReportAndDeriveDocumentCode()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using PpapDbContext dbContext = CreateContext(connection, TenantId);
        await dbContext.Database.EnsureCreatedAsync();
        ReportFixture completed = CreateReportFixture(TenantId, OwnerUserId, true, true, "psw");
        ReportFixture incomplete = CreateReportFixture(TenantId, OwnerUserId, false, false, "dimr");
        dbContext.AddRange(completed.Submission, completed.Job, completed.Report, incomplete.Submission, incomplete.Job, incomplete.Report);
        await dbContext.SaveChangesAsync();
        ICurrentUser currentUser = CurrentUser(CreatorUserId);
        CreateManualPpapFindingCommandHandler handler = new(dbContext, currentUser);
        PpapSubmissionDocument document = completed.Document!;
        CreateManualPpapFindingCommand command = new(
            completed.Report.Id,
            "Manual dimensional issue",
            "Measured evidence",
            document.Id,
            document.CanonicalDocumentId,
            document.ExtractionAttemptId);

        Guid findingId = await handler.Handle(command, CancellationToken.None);
        PpapManualFinding finding = await dbContext.PpapManualFindings.AsNoTracking().SingleAsync(x => x.Id == findingId);
        GetPpapReportQueryHandler reportHandler = new(dbContext, CurrentUser(OwnerUserId));
        PpapReportDto report = await reportHandler.Handle(new GetPpapReportQuery(completed.Report.Id), CancellationToken.None);
        await Should.ThrowAsync<NotFoundException>(async () => await handler.Handle(
            new CreateManualPpapFindingCommand(incomplete.Report.Id, "Issue", null, null, null, null),
            CancellationToken.None));

        finding.DocumentCode.ShouldBe("PSW");
        finding.ReportId.ShouldBe(completed.Report.Id);
        finding.SubmissionId.ShouldBe(completed.Submission.Id);
        finding.JobId.ShouldBe(completed.Job.Id);
        finding.OwnerUserId.ShouldBe(OwnerUserId);
        finding.CreatedByUserId.ShouldBe(CreatorUserId);
        report.ManualFindingCount.ShouldBe(1);
        report.FindingCount.ShouldBe(1);
        report.OpenFindingCount.ShouldBe(1);
        report.ManualFindings.ShouldHaveSingleItem().DocumentCode.ShouldBe("PSW");
    }

    [Fact]
    public async Task Handle_Should_RejectTenantAndDocumentCorrelationIsolation()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using (PpapDbContext tenantContext = CreateContext(connection, TenantId))
        {
            await tenantContext.Database.EnsureCreatedAsync();
        }
        ReportFixture otherTenantFixture = CreateReportFixture(OtherTenantId, OwnerUserId, true, true, "psw");
        await using (PpapDbContext otherContext = CreateContext(connection, OtherTenantId))
        {
            otherContext.AddRange(otherTenantFixture.Submission, otherTenantFixture.Job, otherTenantFixture.Report);
            await otherContext.SaveChangesAsync();
        }
        ReportFixture ownFixture = CreateReportFixture(TenantId, OwnerUserId, true, false, "dimr");
        await using PpapDbContext dbContext = CreateContext(connection, TenantId);
        dbContext.AddRange(ownFixture.Submission, ownFixture.Job, ownFixture.Report);
        await dbContext.SaveChangesAsync();
        CreateManualPpapFindingCommandHandler handler = new(dbContext, CurrentUser(CreatorUserId));

        await Should.ThrowAsync<NotFoundException>(async () => await handler.Handle(
            new CreateManualPpapFindingCommand(otherTenantFixture.Report.Id, "Issue", null, null, null, null),
            CancellationToken.None));
        await Should.ThrowAsync<NotFoundException>(async () => await handler.Handle(
            new CreateManualPpapFindingCommand(
                ownFixture.Report.Id,
                "Issue",
                null,
                otherTenantFixture.Document!.Id,
                otherTenantFixture.Document.CanonicalDocumentId,
                otherTenantFixture.Document.ExtractionAttemptId),
            CancellationToken.None));
        (await dbContext.PpapManualFindings.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Should_RejectSelfWaiverWrongReportTargetsAndDuplicateActiveWaiver()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using PpapDbContext dbContext = CreateContext(connection, TenantId);
        await dbContext.Database.EnsureCreatedAsync();
        ReportFixture first = CreateReportFixture(TenantId, OwnerUserId, true, false, "psw");
        ReportFixture second = CreateReportFixture(TenantId, OwnerUserId, true, false, "dimr");
        PpapManualFinding manual = CreateManual(first, CreatorUserId);
        PpapManualFinding otherManual = CreateManual(second, CreatorUserId);
        PpapFinding automated = CreateAutomated(second);
        dbContext.AddRange(first.Submission, first.Job, first.Report, second.Submission, second.Job, second.Report, manual, otherManual, automated);
        await dbContext.SaveChangesAsync();

        WaivePpapFindingCommand manualCommand = new(first.Report.Id, PpapFindingKind.Manual, manual.Id, "Accepted deviation");
        WaivePpapFindingCommandHandler selfHandler = new(dbContext, CurrentUser(CreatorUserId));
        await Should.ThrowAsync<ForbiddenException>(async () => await selfHandler.Handle(manualCommand, CancellationToken.None));

        WaivePpapFindingCommandHandler approverHandler = new(dbContext, CurrentUser(ApproverUserId));
        await Should.ThrowAsync<NotFoundException>(async () => await approverHandler.Handle(
            new WaivePpapFindingCommand(first.Report.Id, PpapFindingKind.Manual, otherManual.Id, "Wrong report"),
            CancellationToken.None));
        await Should.ThrowAsync<NotFoundException>(async () => await approverHandler.Handle(
            new WaivePpapFindingCommand(first.Report.Id, PpapFindingKind.Automated, automated.Id, "Wrong report"),
            CancellationToken.None));

        Guid waiverId = await approverHandler.Handle(manualCommand, CancellationToken.None);
        await Should.ThrowAsync<CustomException>(async () => await approverHandler.Handle(manualCommand, CancellationToken.None));
        PpapFindingWaiver waiver = await dbContext.PpapFindingWaivers.AsNoTracking().SingleAsync(x => x.Id == waiverId);
        waiver.IsActive.ShouldBeTrue();
        waiver.ApprovedByUserId.ShouldBe(ApproverUserId);
        waiver.Reason.ShouldBe("Accepted deviation");
    }

    [Fact]
    public async Task Handle_Should_EnforceFilteredActiveUniquenessAtDatabase()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using PpapDbContext dbContext = CreateContext(connection, TenantId);
        await dbContext.Database.EnsureCreatedAsync();
        ReportFixture fixture = CreateReportFixture(TenantId, OwnerUserId, true, false, "psw");
        PpapManualFinding manual = CreateManual(fixture, CreatorUserId);
        PpapFindingWaiver first = CreateWaiver(fixture, manual.Id, "First");
        PpapFindingWaiver duplicate = CreateWaiver(fixture, manual.Id, "Duplicate");
        dbContext.AddRange(fixture.Submission, fixture.Job, fixture.Report, manual, first, duplicate);

        await Should.ThrowAsync<DbUpdateException>(async () => await dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task Handle_Should_RevokeOncePreserveApprovalAppendRevocationAndAllowRewaive()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using PpapDbContext dbContext = CreateContext(connection, TenantId);
        await dbContext.Database.EnsureCreatedAsync();
        ReportFixture fixture = CreateReportFixture(TenantId, OwnerUserId, true, false, "psw");
        PpapManualFinding manual = CreateManual(fixture, CreatorUserId);
        PpapFindingWaiver originalWaiver = CreateWaiver(fixture, manual.Id, "Original approval");
        Guid originalApprover = originalWaiver.ApprovedByUserId;
        DateTime originalApprovedAt = originalWaiver.ApprovedAtUtc;
        dbContext.AddRange(fixture.Submission, fixture.Job, fixture.Report, manual, originalWaiver);
        await dbContext.SaveChangesAsync();
        RevokePpapFindingWaiverCommand command = new(fixture.Report.Id, originalWaiver.Id, "Evidence changed");
        RevokePpapFindingWaiverCommandHandler revokeHandler = new(dbContext, CurrentUser(RevokerUserId));

        Guid revocationId = await revokeHandler.Handle(command, CancellationToken.None);
        dbContext.ChangeTracker.Clear();
        PpapFindingWaiver revokedWaiver = await dbContext.PpapFindingWaivers.AsNoTracking().SingleAsync(x => x.Id == originalWaiver.Id);
        PpapWaiverRevocation revocation = await dbContext.PpapWaiverRevocations.AsNoTracking().SingleAsync(x => x.Id == revocationId);
        await Should.ThrowAsync<CustomException>(async () => await revokeHandler.Handle(command, CancellationToken.None));
        WaivePpapFindingCommandHandler waiveHandler = new(dbContext, CurrentUser(Guid.NewGuid()));
        Guid replacementId = await waiveHandler.Handle(
            new WaivePpapFindingCommand(fixture.Report.Id, PpapFindingKind.Manual, manual.Id, "New approval"),
            CancellationToken.None);

        revokedWaiver.IsActive.ShouldBeFalse();
        revokedWaiver.ApprovedByUserId.ShouldBe(originalApprover);
        revokedWaiver.Reason.ShouldBe("Original approval");
        revokedWaiver.ApprovedAtUtc.ShouldBe(originalApprovedAt);
        revocation.WaiverId.ShouldBe(originalWaiver.Id);
        revocation.RevokedByUserId.ShouldBe(RevokerUserId);
        revocation.Reason.ShouldBe("Evidence changed");
        revocation.RevokedAtUtc.ShouldBeInRange(DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        replacementId.ShouldNotBe(originalWaiver.Id);
        (await dbContext.PpapFindingWaivers.CountAsync(x => x.TargetFindingId == manual.Id && x.IsActive)).ShouldBe(1);
        (await dbContext.PpapFindingWaivers.CountAsync(x => x.TargetFindingId == manual.Id)).ShouldBe(2);
    }

    [Fact]
    public async Task Handle_Should_ObservePreCancelledToken()
    {
        await using SqliteConnection connection = await OpenConnectionAsync();
        await using PpapDbContext dbContext = CreateContext(connection, TenantId);
        await dbContext.Database.EnsureCreatedAsync();
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        CreateManualPpapFindingCommandHandler handler = new(dbContext, CurrentUser(CreatorUserId));

        await Should.ThrowAsync<OperationCanceledException>(async () => await handler.Handle(
            new CreateManualPpapFindingCommand(Guid.NewGuid(), "Issue", null, null, null, null),
            cancellation.Token));
    }

    private static PpapManualFinding CreateManual(ReportFixture fixture, Guid creatorId) => PpapManualFinding.Create(
        fixture.Report.TenantId,
        fixture.Report.Id,
        fixture.Submission.Id,
        fixture.Job.Id,
        fixture.Report.OwnerUserId,
        "Manual issue",
        "Evidence",
        creatorId,
        DateTime.UtcNow);

    private static PpapFindingWaiver CreateWaiver(ReportFixture fixture, Guid findingId, string reason) => PpapFindingWaiver.Create(
        fixture.Report.TenantId,
        fixture.Report.Id,
        fixture.Submission.Id,
        fixture.Job.Id,
        fixture.Report.OwnerUserId,
        PpapFindingTargetType.Manual,
        findingId,
        ApproverUserId,
        reason,
        DateTime.UtcNow);

    private static PpapFinding CreateAutomated(ReportFixture fixture) => PpapFinding.Create(
        fixture.Report.TenantId,
        fixture.Submission.Id,
        fixture.Job.Id,
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        fixture.Report.OwnerUserId,
        "RULE-1",
        "Rule",
        null,
        PpapFindingOutcome.Failed,
        "Failed",
        "Reason",
        "[]",
        DateTime.UtcNow);

    private static ReportFixture CreateReportFixture(string tenantId, Guid ownerId, bool completed, bool withExtraction, string documentCode)
    {
        PpapSubmission submission = PpapSubmission.CreateDraft(tenantId, ownerId, "Part", $"PN-{Guid.NewGuid():N}", "A", 3);
        PpapSubmissionDocument document = submission.AddDocumentSlot(documentCode);
        submission.ApplyFinalizedFile(document.Id, Guid.NewGuid(), true);
        if (withExtraction)
        {
            DateTime now = DateTime.UtcNow;
            document.MarkExtractionRequestPublished(Guid.NewGuid(), now);
            document.RecordExtractionCompleted(tenantId, documentCode, Guid.NewGuid(), Guid.NewGuid(), null, null, now.AddSeconds(1));
        }
        submission.Submit();
        PpapJob job = PpapJob.CreateQueued(tenantId, submission.Id, submission.Version, ownerId);
        if (completed) Complete(job);
        PpapReport report = PpapReport.CreateFixture(tenantId, submission.Id, job.Id, submission.Version, ownerId, 1, 0, 0, 0, 0, 0, 0);
        return new ReportFixture(submission, document, job, report);
    }

    private static void Complete(PpapJob job)
    {
        job.Start();
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 10);
        job.AdvanceStage(PpapJobStage.DocumentClassification, 30);
        job.AdvanceStage(PpapJobStage.CrossValidation, 50);
        job.AdvanceStage(PpapJobStage.ReportGeneration, 70);
        job.AdvanceStage(PpapJobStage.NotificationDelivery, 90);
        job.Complete();
    }

    private static ICurrentUser CurrentUser(Guid userId)
    {
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.GetUserId().Returns(userId);
        return currentUser;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static PpapDbContext CreateContext(SqliteConnection connection, string tenantId)
    {
        DbContextOptions<PpapDbContext> options = new DbContextOptionsBuilder<PpapDbContext>().UseSqlite(connection).Options;
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        return new PpapDbContext(new TestTenantAccessor(tenantId), options, Options.Create(new DatabaseOptions()), environment);
    }

    private sealed record ReportFixture(PpapSubmission Submission, PpapSubmissionDocument? Document, PpapJob Job, PpapReport Report);

    private sealed class TestTenantAccessor(string tenantId) : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(new AppTenantInfo(tenantId, tenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }
}
