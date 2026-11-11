using System.Security.Claims;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Persistence;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Features;
using DES.Modules.Rules.Persistence;
using DES.Modules.Rules.Runtime;
using Finbuckle.MultiTenant;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Rules.Tests;

public sealed class RuleEvaluationServiceTests
{
    private const string TenantId = "tenant-a";
    private const string Ast = """{"type":"comparison","operator":"Equal","left":{"type":"literal","value":1},"right":{"type":"literal","value":1}}""";

    [Fact]
    public async Task EvaluateAsync_Should_UseRetiredSnapshotAndPreserveMissingDocumentSemantics()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedSnapshot();
        template.Retire(templateVersionId, template.Version, DateTime.UtcNow.AddMinutes(1));
        rule.Retire(rule.CurrentPublishedVersionId!.Value, rule.Version, DateTime.UtcNow.AddMinutes(1));
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);
        var request = new RuleEvaluationRequest(
            templateVersionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "v1",
            new Dictionary<string, string>());

        Guid runId = await service.EvaluateAsync(request);

        RuleEvaluationRun run = await testContext.Db.RuleEvaluationRuns
            .Include(x => x.Results)
            .SingleAsync(x => x.Id == runId);
        RuleEvaluationResultRecord result = run.Results.ShouldHaveSingleItem();
        result.RuleVersionId.ShouldBe(rule.Versions.Single().Id);
        result.Status.ShouldBe(RuleEvaluationStatus.NotEvaluated);
        result.Reason.ShouldBe("Assigned document was not supplied.");

        var reader = new RuleEvaluationResultReader(testContext.Db, testContext.TenantAccessor);
        RuleEvaluationRunDetails details = await reader.ReadAsync(runId, request.OwnerUserId, request.SubjectId);
        RuleEvaluationResultDetails detail = details.Results.ShouldHaveSingleItem();
        details.TemplateVersionId.ShouldBe(templateVersionId);
        detail.Id.ShouldBe(result.Id);
        detail.RuleDefinitionId.ShouldBe(rule.Id);
        detail.StableRuleCode.ShouldBe("RULE-1");
        detail.RuleName.ShouldBe("Rule");
        detail.Message.ShouldBe("Document could not be evaluated");
        detail.TraceJson.ShouldBe("[]");
    }

    [Fact]
    public async Task EvaluateAsync_Should_IsolateParametersPerAssignment()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedParameterizedSnapshot();
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);
        var request = new RuleEvaluationRequest(
            templateVersionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "v1",
            new Dictionary<string, string> { ["PSW"] = "{}", ["FMEA"] = "{}" });

        Guid runId = await service.EvaluateAsync(request);

        RuleEvaluationRun run = await testContext.Db.RuleEvaluationRuns.Include(x => x.Results).SingleAsync(x => x.Id == runId);
        run.Status.ShouldBe(EvaluationRunStatus.Completed);
        RuleEvaluationResultRecord psw = run.Results.Single(x => x.DocumentCode == "PSW");
        RuleEvaluationResultRecord fmea = run.Results.Single(x => x.DocumentCode == "FMEA");
        psw.Status.ShouldBe(RuleEvaluationStatus.Passed);
        fmea.Status.ShouldBe(RuleEvaluationStatus.Failed);
        psw.TraceJson.ShouldContain("\"Left\":\"1\"");
        fmea.TraceJson.ShouldContain("\"Left\":\"2\"");
    }

    [Fact]
    public async Task EvaluateAsync_Should_RecordSanitizedErrorAndContinue_WhenStoredParametersAreMalformed()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedParameterizedSnapshot();
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        Guid assignmentId = template.Versions.Single().RuleAssignments.Single(x => x.DocumentCode == "PSW").Id;
        await testContext.Db.Database.ExecuteSqlRawAsync("UPDATE \"TemplateRuleAssignments\" SET \"ParametersJson\" = {0} WHERE \"Id\" = {1}", "{", assignmentId);
        testContext.Db.ChangeTracker.Clear();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);
        var request = new RuleEvaluationRequest(
            templateVersionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "v1",
            new Dictionary<string, string> { ["PSW"] = "{}", ["FMEA"] = "{}" });

        Guid runId = await service.EvaluateAsync(request);

        RuleEvaluationRun run = await testContext.Db.RuleEvaluationRuns.Include(x => x.Results).SingleAsync(x => x.Id == runId);
        RuleEvaluationResultRecord malformed = run.Results.Single(x => x.DocumentCode == "PSW");
        RuleEvaluationResultRecord isolated = run.Results.Single(x => x.DocumentCode == "FMEA");
        malformed.Status.ShouldBe(RuleEvaluationStatus.Error);
        malformed.Reason.ShouldBe("Stored assignment parameters are invalid.");
        isolated.Status.ShouldBe(RuleEvaluationStatus.Failed);
        run.Status.ShouldBe(EvaluationRunStatus.PartiallyCompleted);
    }

    [Fact]
    public async Task EvaluateAsync_Should_RecordErrorAndContinue_WhenAssignmentParameterIsMissing()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedParameterizedSnapshot("{}");
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);
        var request = new RuleEvaluationRequest(
            templateVersionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "v1",
            new Dictionary<string, string> { ["PSW"] = "{}", ["FMEA"] = "{}" });

        Guid runId = await service.EvaluateAsync(request);

        RuleEvaluationRun run = await testContext.Db.RuleEvaluationRuns.Include(x => x.Results).SingleAsync(x => x.Id == runId);
        RuleEvaluationResultRecord missing = run.Results.Single(x => x.DocumentCode == "PSW");
        RuleEvaluationResultRecord isolated = run.Results.Single(x => x.DocumentCode == "FMEA");
        missing.Status.ShouldBe(RuleEvaluationStatus.Error);
        missing.Reason.ShouldBe("Parameter was not supplied.");
        isolated.Status.ShouldBe(RuleEvaluationStatus.Failed);
        run.Status.ShouldBe(EvaluationRunStatus.PartiallyCompleted);
    }

    [Fact]
    public async Task ReadAsync_Should_NotReturnEvaluationForDifferentOwner()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedSnapshot();
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);
        var request = new RuleEvaluationRequest(templateVersionId, Guid.NewGuid(), Guid.NewGuid(), "v1", new Dictionary<string, string>());
        Guid runId = await service.EvaluateAsync(request);
        var reader = new RuleEvaluationResultReader(testContext.Db, testContext.TenantAccessor);

        await Should.ThrowAsync<NotFoundException>(() => reader.ReadAsync(runId, Guid.NewGuid(), request.SubjectId));
    }

    [Fact]
    public async Task EvaluateAsync_Should_ReturnExistingRun_WhenSubjectAndTemplateVersionMatch()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedSnapshot();
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);
        Guid subjectId = Guid.NewGuid();
        var request = new RuleEvaluationRequest(
            templateVersionId,
            subjectId,
            Guid.NewGuid(),
            "v1",
            new Dictionary<string, string> { ["PSW"] = "{}" });

        Guid firstRunId = await service.EvaluateAsync(request);
        Guid retryRunId = await service.EvaluateAsync(request with
        {
            OwnerUserId = Guid.NewGuid(),
            InputSchemaVersion = "v2",
            Documents = new Dictionary<string, string>(),
        });

        retryRunId.ShouldBe(firstRunId);
        (await testContext.Db.RuleEvaluationRuns.CountAsync(x => x.SubjectId == subjectId && x.TemplateVersionId == templateVersionId)).ShouldBe(1);
    }

    [Fact]
    public async Task EvaluateAsync_Should_RejectMissingAmbientTenant()
    {
        await using TestContext testContext = await CreateTestContextAsync(null);
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);
        var request = new RuleEvaluationRequest(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "v1",
            new Dictionary<string, string>());

        await Should.ThrowAsync<UnauthorizedException>(() => service.EvaluateAsync(request));
    }

    [Fact]
    public async Task EvaluateAsync_Should_SkipArchivedRule_AndEvaluateOthers()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition kept, RuleDefinition archived, RuleTemplate template, Guid templateVersionId) = CreateTwoRuleSnapshot();
        archived.Archive(archived.Version, Guid.NewGuid(), DateTime.UtcNow);
        testContext.Db.AddRange(kept, archived, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);

        Guid runId = await service.EvaluateAsync(new RuleEvaluationRequest(templateVersionId, Guid.NewGuid(), Guid.NewGuid(), "v1", new Dictionary<string, string> { ["PSW"] = "{}" }));

        RuleEvaluationRun run = await testContext.Db.RuleEvaluationRuns.Include(x => x.Results).SingleAsync(x => x.Id == runId);
        run.Results.ShouldHaveSingleItem().RuleDefinitionId.ShouldBe(kept.Id);
        run.Status.ShouldBe(EvaluationRunStatus.Completed);
    }

    [Fact]
    public async Task PreviewAsync_Should_EvaluateOnlyTargetDocumentAssignments_WithoutPersistingRun()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedParameterizedSnapshot();
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);

        IReadOnlyList<RuleEvaluationPreviewResult> results = await service.PreviewAsync(
            new RuleEvaluationRequest(templateVersionId, Guid.NewGuid(), Guid.NewGuid(), "v1", new Dictionary<string, string> { ["PSW"] = "{}", ["FMEA"] = "{}" }),
            "psw");

        RuleEvaluationPreviewResult result = results.ShouldHaveSingleItem();
        result.DocumentCode.ShouldBe("PSW");
        result.Status.ShouldBe(RuleEvaluationStatus.Passed);
        result.StableRuleCode.ShouldBe("RULE-PARAM");
        (await testContext.Db.RuleEvaluationRuns.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task PreviewAsync_Should_SkipComplexExecutorAssignments()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedParameterizedSnapshot(
            pswParameters: "{\"expected\":1,\"executorKey\":\"pfmea-action-dates\"}");
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);

        IReadOnlyList<RuleEvaluationPreviewResult> results = await service.PreviewAsync(
            new RuleEvaluationRequest(templateVersionId, Guid.NewGuid(), Guid.NewGuid(), "v1", new Dictionary<string, string> { ["PSW"] = "{}" }),
            "PSW");

        results.ShouldBeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_Should_SkipArchivedRules()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition kept, RuleDefinition archived, RuleTemplate template, Guid templateVersionId) = CreateTwoRuleSnapshot();
        archived.Archive(archived.Version, Guid.NewGuid(), DateTime.UtcNow);
        testContext.Db.AddRange(kept, archived, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);

        IReadOnlyList<RuleEvaluationPreviewResult> results = await service.PreviewAsync(
            new RuleEvaluationRequest(templateVersionId, Guid.NewGuid(), Guid.NewGuid(), "v1", new Dictionary<string, string> { ["PSW"] = "{}" }),
            "PSW");

        results.ShouldHaveSingleItem().RuleDefinitionId.ShouldBe(kept.Id);
    }

    [Fact]
    public async Task DeleteRule_Should_HardDeletePublishedRule_AndRemoveTemplateAssignments_WhenNeverEvaluated()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedSnapshot();
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        testContext.Db.ChangeTracker.Clear();
        var handler = new DeleteRuleCommandHandler(testContext.Db, new TestUser(Guid.NewGuid()));

        DeleteRuleResultDto result = await handler.Handle(new DeleteRuleCommand(rule.Id, rule.Version), CancellationToken.None);

        result.Outcome.ShouldBe(DeleteRuleOutcome.Deleted);
        (await testContext.Db.RuleDefinitions.AnyAsync(x => x.Id == rule.Id)).ShouldBeFalse();
        (await testContext.Db.RuleVersions.AnyAsync(x => x.RuleDefinitionId == rule.Id)).ShouldBeFalse();
        (await testContext.Db.TemplateRuleAssignments.AnyAsync(x => x.TemplateVersionId == templateVersionId)).ShouldBeFalse();
        (await testContext.Db.RuleTemplateVersions.AnyAsync(x => x.Id == templateVersionId)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteRule_Should_Archive_AndKeepHistory_WhenRuleWasEvaluated()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition rule, RuleTemplate template, Guid templateVersionId) = CreatePublishedSnapshot();
        testContext.Db.AddRange(rule, template);
        await testContext.Db.SaveChangesAsync();
        var service = new RuleEvaluationService(testContext.Db, testContext.TenantAccessor, TimeProvider.System);
        Guid runId = await service.EvaluateAsync(new RuleEvaluationRequest(templateVersionId, Guid.NewGuid(), Guid.NewGuid(), "v1", new Dictionary<string, string> { ["PSW"] = "{}" }));
        testContext.Db.ChangeTracker.Clear();
        Guid userId = Guid.NewGuid();
        var handler = new DeleteRuleCommandHandler(testContext.Db, new TestUser(userId));

        DeleteRuleResultDto result = await handler.Handle(new DeleteRuleCommand(rule.Id, rule.Version), CancellationToken.None);

        result.Outcome.ShouldBe(DeleteRuleOutcome.Archived);
        RuleDefinition stored = await testContext.Db.RuleDefinitions.AsNoTracking().SingleAsync(x => x.Id == rule.Id);
        stored.ArchivedAtUtc.ShouldNotBeNull();
        stored.ArchivedByUserId.ShouldBe(userId);
        (await testContext.Db.RuleEvaluationResults.CountAsync(x => x.RuleEvaluationRunId == runId)).ShouldBe(1);
        (await testContext.Db.TemplateRuleAssignments.AnyAsync(x => x.TemplateVersionId == templateVersionId)).ShouldBeTrue();

        testContext.Db.ChangeTracker.Clear();
        await new RestoreRuleCommandHandler(testContext.Db).Handle(new RestoreRuleCommand(rule.Id, stored.Version), CancellationToken.None);
        (await testContext.Db.RuleDefinitions.AsNoTracking().SingleAsync(x => x.Id == rule.Id)).ArchivedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task SearchRules_Should_HideArchivedByDefault_AndFilterOnRequest()
    {
        await using TestContext testContext = await CreateTestContextAsync();
        (RuleDefinition kept, RuleDefinition archived, RuleTemplate template, _) = CreateTwoRuleSnapshot();
        archived.Archive(archived.Version, Guid.NewGuid(), DateTime.UtcNow);
        testContext.Db.AddRange(kept, archived, template);
        await testContext.Db.SaveChangesAsync();
        var handler = new SearchRulesQueryHandler(testContext.Db);

        (await handler.Handle(new SearchRulesQuery(), CancellationToken.None)).Items.ShouldHaveSingleItem().Id.ShouldBe(kept.Id);
        RuleDefinitionSummaryDto only = (await handler.Handle(new SearchRulesQuery { Archived = RuleArchiveFilter.Only }, CancellationToken.None)).Items.ShouldHaveSingleItem();
        only.Id.ShouldBe(archived.Id);
        only.IsArchived.ShouldBeTrue();
        (await handler.Handle(new SearchRulesQuery { Archived = RuleArchiveFilter.Include }, CancellationToken.None)).Items.Count.ShouldBe(2);
    }

    private static (RuleDefinition Kept, RuleDefinition Archived, RuleTemplate Template, Guid TemplateVersionId) CreateTwoRuleSnapshot()
    {
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        RuleDefinition kept = RuleDefinition.Create(TenantId, "RULE-KEEP", "Kept", null, RuleCategory.Document, Ast, "{}", userId, now);
        RuleDefinition archived = RuleDefinition.Create(TenantId, "RULE-ARCH", "Archived", null, RuleCategory.Document, Ast, "{}", userId, now);
        foreach (RuleDefinition rule in new[] { kept, archived })
        {
            Guid versionId = rule.Versions.Single().Id;
            rule.SubmitForReview(versionId, rule.Version, now);
            rule.Publish(versionId, rule.Version, true, userId, now);
        }
        RuleTemplate template = RuleTemplate.Create(TenantId, "TEMPLATE-TWO", "Template", null, userId, now);
        Guid templateVersionId = template.Versions.Single().Id;
        template.ConfigureDraft(
            templateVersionId,
            template.Version,
            "Template",
            null,
            [new TemplateDocumentRequirementInput("PSW", true, 1)],
            [new TemplateRuleAssignmentInput(kept.Versions.Single().Id, "PSW", "{}"), new TemplateRuleAssignmentInput(archived.Versions.Single().Id, "PSW", "{}")],
            now);
        template.SubmitForReview(templateVersionId, template.Version, now);
        template.Publish(templateVersionId, template.Version, true, userId, now);
        return (kept, archived, template, templateVersionId);
    }

    private sealed class TestUser(Guid userId) : ICurrentUser
    {
        public string? Name => "Test User";
        public Guid GetUserId() => userId;
        public string? GetUserEmail() => null;
        public string? GetTenant() => TenantId;
        public bool IsAuthenticated() => true;
        public bool IsInRole(string role) => false;
        public IEnumerable<Claim>? GetUserClaims() => null;
    }

    private static (RuleDefinition Rule, RuleTemplate Template, Guid TemplateVersionId) CreatePublishedSnapshot()
    {
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        RuleDefinition rule = RuleDefinition.Create(TenantId, "RULE-1", "Rule", null, RuleCategory.Document, Ast, "{\"notEvaluated\":{\"message\":\"Document could not be evaluated\"}}", userId, now);
        Guid ruleVersionId = rule.Versions.Single().Id;
        rule.SubmitForReview(ruleVersionId, rule.Version, now);
        rule.Publish(ruleVersionId, rule.Version, true, userId, now);

        RuleTemplate template = RuleTemplate.Create(TenantId, "TEMPLATE-1", "Template", null, userId, now);
        Guid templateVersionId = template.Versions.Single().Id;
        template.ConfigureDraft(
            templateVersionId,
            template.Version,
            "Template",
            null,
            [new TemplateDocumentRequirementInput("PSW", true, 1)],
            [new TemplateRuleAssignmentInput(ruleVersionId, "PSW", "{}")],
            now);
        template.SubmitForReview(templateVersionId, template.Version, now);
        template.Publish(templateVersionId, template.Version, true, userId, now);
        return (rule, template, templateVersionId);
    }

    private static (RuleDefinition Rule, RuleTemplate Template, Guid TemplateVersionId) CreatePublishedParameterizedSnapshot(
        string pswParameters = "{\"expected\":1}",
        string fmeaParameters = "{\"expected\":2}")
    {
        const string ast = """{"type":"comparison","operator":"Equal","left":{"type":"parameter","name":"expected"},"right":{"type":"literal","value":1}}""";
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        RuleDefinition rule = RuleDefinition.Create(TenantId, "RULE-PARAM", "Parameterized Rule", null, RuleCategory.Document, ast, "{}", userId, now);
        Guid ruleVersionId = rule.Versions.Single().Id;
        rule.SubmitForReview(ruleVersionId, rule.Version, now);
        rule.Publish(ruleVersionId, rule.Version, true, userId, now);

        RuleTemplate template = RuleTemplate.Create(TenantId, "TEMPLATE-PARAM", "Parameterized Template", null, userId, now);
        Guid templateVersionId = template.Versions.Single().Id;
        template.ConfigureDraft(
            templateVersionId,
            template.Version,
            "Parameterized Template",
            null,
            [new TemplateDocumentRequirementInput("PSW", true, 1), new TemplateDocumentRequirementInput("FMEA", true, 2)],
            [new TemplateRuleAssignmentInput(ruleVersionId, "PSW", pswParameters), new TemplateRuleAssignmentInput(ruleVersionId, "FMEA", fmeaParameters)],
            now);
        template.SubmitForReview(templateVersionId, template.Version, now);
        template.Publish(templateVersionId, template.Version, true, userId, now);
        return (rule, template, templateVersionId);
    }

    private static async Task<TestContext> CreateTestContextAsync(string? tenantId = TenantId)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenantAccessor = new TestTenantAccessor(tenantId);
        DbContextOptions<RulesDbContext> options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseSqlite(connection)
            .Options;
        var environment = new TestHostEnvironment();
        var db = new RulesDbContext(tenantAccessor, options, Options.Create(new DatabaseOptions()), environment);
        await db.Database.EnsureCreatedAsync();
        return new TestContext(connection, db, tenantAccessor);
    }

    private sealed class TestTenantAccessor(string? tenantId) : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(tenantId is null ? null : new AppTenantInfo(tenantId, tenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Rules.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class TestContext(SqliteConnection connection, RulesDbContext db, TestTenantAccessor tenantAccessor) : IAsyncDisposable
    {
        public RulesDbContext Db { get; } = db;
        public TestTenantAccessor TenantAccessor { get; } = tenantAccessor;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

public sealed class EvaluateTemplateCommandHandlerTests
{
    [Fact]
    public async Task Handle_Should_DelegateTenantSafeMetadata_WhenOwnerIsAuthenticatedUser()
    {
        Guid ownerUserId = Guid.NewGuid();
        var evaluationService = new CapturingEvaluationService();
        var handler = new EvaluateTemplateCommandHandler(evaluationService, new TestCurrentUser(ownerUserId));
        var command = new EvaluateTemplateCommand(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ownerUserId,
            "v1",
            new Dictionary<string, string> { ["PSW"] = "{}" });
        using var cancellation = new CancellationTokenSource();

        Guid result = await handler.Handle(command, cancellation.Token);

        result.ShouldBe(evaluationService.RunId);
        evaluationService.Request.ShouldBe(new RuleEvaluationRequest(
            command.TemplateVersionId,
            command.SubjectId,
            command.OwnerUserId,
            command.InputSchemaVersion,
            command.Documents));
        evaluationService.CancellationToken.ShouldBe(cancellation.Token);
    }

    private sealed class CapturingEvaluationService : IRuleEvaluationService
    {
        public Guid RunId { get; } = Guid.NewGuid();
        public RuleEvaluationRequest? Request { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<Guid> EvaluateAsync(RuleEvaluationRequest request, CancellationToken cancellationToken = default)
        {
            Request = request;
            CancellationToken = cancellationToken;
            return Task.FromResult(RunId);
        }

        public Task<IReadOnlyList<RuleEvaluationPreviewResult>> PreviewAsync(
            RuleEvaluationRequest request,
            string documentCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RuleEvaluationPreviewResult>>([]);
    }

    private sealed class TestCurrentUser(Guid userId) : ICurrentUser
    {
        public string? Name => "Test User";
        public Guid GetUserId() => userId;
        public string? GetUserEmail() => null;
        public string? GetTenant() => null;
        public bool IsAuthenticated() => true;
        public bool IsInRole(string role) => false;
        public IEnumerable<Claim>? GetUserClaims() => null;
    }
}
