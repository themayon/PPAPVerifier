using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts.Events;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
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

namespace Ppap.Tests.Processing;

public sealed class ComplexValidationResultProcessorTests
{
    private const string TenantId = "tenant-1";

    [Fact]
    public async Task ProcessAsync_Should_CreateFindingOnce_When_ResultFails()
    {
        await using TestContext test = await CreateTestContextAsync();
        var sut = new ComplexValidationResultProcessor(test.Db);
        ComplexValidationCompletedIntegrationEvent result = Completed("Failed", "fingerprint-1");

        await sut.ProcessAsync(result, CancellationToken.None);
        await sut.ProcessAsync(result with { Id = Guid.CreateVersion7() }, CancellationToken.None);

        PpapWorkingFinding finding = await test.Db.PpapWorkingFindings.SingleAsync();
        finding.Status.ShouldBe(WorkingFindingStatus.Failed);
        finding.Message.ShouldBe("Validation failed.");
        finding.IsCurrent.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Passed")]
    [InlineData("NotEvaluated")]
    public async Task ProcessAsync_Should_SupersedeFindingWithoutReplacement_When_ResultIsTerminalSuccess(string status)
    {
        await using TestContext test = await CreateTestContextAsync();
        ComplexValidationCompletedIntegrationEvent failed = Completed("Failed", "fingerprint-1");
        var sut = new ComplexValidationResultProcessor(test.Db);
        await sut.ProcessAsync(failed, CancellationToken.None);

        await sut.ProcessAsync(Completed(status, "fingerprint-2") with
        {
            RuleVersionId = failed.RuleVersionId,
            SubmissionId = failed.SubmissionId
        }, CancellationToken.None);

        PpapWorkingFinding finding = await test.Db.PpapWorkingFindings.SingleAsync();
        finding.IsCurrent.ShouldBeFalse();
        finding.SupersededAtUtc.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Error", WorkingFindingStatus.Error)]
    [InlineData("Unexpected", WorkingFindingStatus.Error)]
    public async Task ProcessAsync_Should_CreateErrorFinding_When_ResultIsNotFailedOrTerminalSuccess(
        string status,
        WorkingFindingStatus expected)
    {
        await using TestContext test = await CreateTestContextAsync();
        var sut = new ComplexValidationResultProcessor(test.Db);

        await sut.ProcessAsync(Completed(status, $"fingerprint-{status}"), CancellationToken.None);

        PpapWorkingFinding finding = await test.Db.PpapWorkingFindings.SingleAsync();
        finding.Status.ShouldBe(expected);
    }

    private static ComplexValidationCompletedIntegrationEvent Completed(string status, string fingerprint) => new(
        Guid.CreateVersion7(),
        DateTime.UtcNow,
        TenantId,
        "correlation",
        "PpapValidationWorker",
        Guid.CreateVersion7(),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Guid.CreateVersion7(),
        Guid.Parse("33333333-3333-3333-3333-333333333333"),
        "RULE-1",
        "Rule",
        "json-path-compare:v1",
        "1.0",
        fingerprint,
        status,
        "Validation failed.",
        10,
        []);

    private static async Task<TestContext> CreateTestContextAsync()
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
        return new TestContext(connection, db);
    }

    private sealed class TestTenantAccessor : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(new AppTenantInfo(TenantId, TenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }

    private sealed class TestContext(SqliteConnection connection, PpapDbContext db) : IAsyncDisposable
    {
        internal PpapDbContext Db { get; } = db;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
