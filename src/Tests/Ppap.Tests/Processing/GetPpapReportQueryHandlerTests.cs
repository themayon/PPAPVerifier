using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Ppap.Features.v1.Reports.GetPpapReport;
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

public sealed class GetPpapReportQueryHandlerTests
{
    private const string TenantId = "tenant-1";
    private static readonly Guid OwnerUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Handle_Should_ExposeTypedManifestAndPreserveOwnerFiltering()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using PpapDbContext dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        PpapSubmission ownSubmission = CreateSubmitted(OwnerUserId, "PN-1");
        Guid otherOwnerUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        PpapSubmission otherSubmission = CreateSubmitted(otherOwnerUserId, "PN-2");
        PpapJob ownJob = CreateCompletedJob(ownSubmission, OwnerUserId);
        PpapJob otherJob = CreateCompletedJob(otherSubmission, otherOwnerUserId);
        string manifestJson = $$"""
            {"schemaVersion":"1.0","jobId":"{{ownJob.Id}}","submissionId":"{{ownSubmission.Id}}","submissionVersion":{{ownSubmission.Version}},"templateVersionId":"44444444-4444-4444-4444-444444444444","evaluation":{"runId":"55555555-5555-5555-5555-555555555555","version":1,"inputSchemaVersion":"1.0","status":"Completed","createdAtUtc":"2025-01-01T00:00:00Z","completedAtUtc":"2025-01-01T00:01:00Z","results":[]},"documents":[]}
            """;
        PpapReport ownReport = PpapReport.Create(
            TenantId,
            ownSubmission.Id,
            ownJob.Id,
            ownSubmission.Version,
            OwnerUserId,
            1,
            0,
            0,
            0,
            0,
            0,
            0,
            manifestJson);
        PpapReport otherReport = PpapReport.CreateFixture(
            TenantId,
            otherSubmission.Id,
            otherJob.Id,
            otherSubmission.Version,
            otherOwnerUserId,
            1,
            0,
            0,
            0,
            0,
            0,
            0);
        PpapManualFinding manualFinding = PpapManualFinding.Create(
            TenantId,
            ownReport.Id,
            ownSubmission.Id,
            ownJob.Id,
            OwnerUserId,
            "Manual issue",
            "Evidence",
            Guid.Parse("77777777-7777-7777-7777-777777777777"),
            DateTime.UtcNow);
        PpapFindingWaiver waiver = PpapFindingWaiver.Create(
            TenantId,
            ownReport.Id,
            ownSubmission.Id,
            ownJob.Id,
            OwnerUserId,
            PpapFindingTargetType.Manual,
            manualFinding.Id,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            "Approved reason",
            DateTime.UtcNow);
        waiver.Deactivate();
        PpapWaiverRevocation revocation = PpapWaiverRevocation.Create(
            TenantId,
            waiver.Id,
            Guid.Parse("99999999-9999-9999-9999-999999999999"),
            "Revoked reason",
            DateTime.UtcNow);
        dbContext.AddRange(ownSubmission, otherSubmission, ownJob, otherJob, ownReport, otherReport, manualFinding, waiver, revocation);
        await dbContext.SaveChangesAsync();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.GetUserId().Returns(OwnerUserId);
        var handler = new GetPpapReportQueryHandler(dbContext, currentUser);

        PpapReportDto result = await handler.Handle(new GetPpapReportQuery(ownReport.Id), CancellationToken.None);
        await Should.ThrowAsync<NotFoundException>(async () =>
            await handler.Handle(new GetPpapReportQuery(otherReport.Id), CancellationToken.None));

        result.ReproducibilityManifest.ShouldNotBeNull();
        result.ReproducibilityManifest.SchemaVersion.ShouldBe("1.0");
        result.ReproducibilityManifest.JobId.ShouldBe(ownReport.JobId);
        result.FindingCount.ShouldBe(1);
        result.AutomatedFindingCount.ShouldBe(0);
        result.ManualFindingCount.ShouldBe(1);
        result.WaivedFindingCount.ShouldBe(0);
        result.OpenFindingCount.ShouldBe(1);
        result.ManualFindings.ShouldHaveSingleItem();
        result.ManualFindings[0].Waivers.ShouldHaveSingleItem();
        result.ManualFindings[0].Waivers[0].Reason.ShouldBe("Approved reason");
        result.ManualFindings[0].Waivers[0].IsActive.ShouldBeFalse();
        result.ManualFindings[0].Waivers[0].Revocation.ShouldNotBeNull();
        result.ManualFindings[0].Waivers[0].Revocation!.Reason.ShouldBe("Revoked reason");
    }

    [Fact]
    public async Task Handle_Should_ReturnNullManifest_ForHistoricalReport()
    {
        await using SqliteConnection connection = new("Data Source=:memory:");
        await connection.OpenAsync();
        await using PpapDbContext dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        PpapSubmission submission = CreateSubmitted(OwnerUserId, "PN-1");
        PpapJob job = CreateCompletedJob(submission, OwnerUserId);
        PpapReport report = PpapReport.CreateFixture(
            TenantId,
            submission.Id,
            job.Id,
            submission.Version,
            OwnerUserId,
            1,
            0,
            0,
            0,
            0,
            0,
            0);
        dbContext.AddRange(submission, job, report);
        await dbContext.SaveChangesAsync();
        ICurrentUser currentUser = Substitute.For<ICurrentUser>();
        currentUser.GetUserId().Returns(OwnerUserId);
        var handler = new GetPpapReportQueryHandler(dbContext, currentUser);

        PpapReportDto result = await handler.Handle(new GetPpapReportQuery(report.Id), CancellationToken.None);

        result.ReproducibilityManifest.ShouldBeNull();
    }

    private static PpapSubmission CreateSubmitted(Guid ownerUserId, string partNumber)
    {
        PpapSubmission submission = PpapSubmission.CreateDraft(TenantId, ownerUserId, "Part", partNumber, "A", 3);
        PpapSubmissionDocument document = submission.AddDocumentSlot("PSW");
        submission.ApplyFinalizedFile(document.Id, Guid.CreateVersion7(), true);
        submission.Submit();
        return submission;
    }

    private static PpapJob CreateCompletedJob(PpapSubmission submission, Guid ownerUserId)
    {
        PpapJob job = PpapJob.CreateQueued(TenantId, submission.Id, submission.Version, ownerUserId);
        job.Start();
        job.AdvanceStage(PpapJobStage.PreprocessingExtraction, 10);
        job.AdvanceStage(PpapJobStage.DocumentClassification, 30);
        job.AdvanceStage(PpapJobStage.CrossValidation, 50);
        job.AdvanceStage(PpapJobStage.ReportGeneration, 70);
        job.AdvanceStage(PpapJobStage.NotificationDelivery, 90);
        job.Complete();
        return job;
    }

    private static PpapDbContext CreateContext(SqliteConnection connection)
    {
        DbContextOptions<PpapDbContext> options = new DbContextOptionsBuilder<PpapDbContext>()
            .UseSqlite(connection)
            .Options;
        IHostEnvironment environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(Environments.Development);
        return new PpapDbContext(
            new TestTenantAccessor(),
            options,
            Options.Create(new DatabaseOptions()),
            environment);
    }

    private sealed class TestTenantAccessor : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(new AppTenantInfo(TenantId, TenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }
}
