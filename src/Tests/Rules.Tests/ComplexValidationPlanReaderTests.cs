using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
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

public sealed class ComplexValidationPlanReaderTests
{
    private const string TenantId = "tenant-a";

    [Fact]
    public async Task ReadAsync_Should_ReturnComplexAssignmentsWithDeterministicDependencies()
    {
        await using TestContext test = await CreateTestContextAsync();
        Guid userId = Guid.NewGuid();
        DateTime now = DateTime.UtcNow;
        const string complexAst = """{"type":"comparison","operator":"Equal","left":{"type":"field","documentCode":"DIMR","path":"measurements.0.nominal"},"right":{"type":"field","documentCode":"DWG","path":"measurements.0.nominal"}}""";
        const string simpleAst = """{"type":"comparison","operator":"Equal","left":{"type":"literal","value":1},"right":{"type":"literal","value":1}}""";
        RuleDefinition complexRule = CreatePublishedRule("DIMR-DWG", "DIMR to DWG", RuleCategory.CrossDocument, complexAst, userId, now);
        RuleDefinition simpleRule = CreatePublishedRule("SIMPLE", "Simple", RuleCategory.Document, simpleAst, userId, now);
        RuleTemplate template = RuleTemplate.Create(TenantId, "TEMPLATE", "Template", null, userId, now);
        Guid templateVersionId = template.Versions.Single().Id;
        template.ConfigureDraft(
            templateVersionId,
            template.Version,
            template.Name,
            template.Description,
            [
                new TemplateDocumentRequirementInput("DIMR", true, 1),
                new TemplateDocumentRequirementInput("DWG", true, 2)
            ],
            [
                new TemplateRuleAssignmentInput(complexRule.CurrentPublishedVersionId!.Value, null, """{"executorKey":"json-path-compare:v1","leftDocumentCode":"DIMR","rightDocumentCode":"DWG","leftPath":"measurements.0.nominal","rightPath":"measurements.0.nominal","operator":"Equal"}"""),
                new TemplateRuleAssignmentInput(simpleRule.CurrentPublishedVersionId!.Value, "DIMR", "{}")
            ],
            now);
        template.SubmitForReview(templateVersionId, template.Version, now);
        template.Publish(templateVersionId, template.Version, true, userId, now);
        test.Db.AddRange(complexRule, simpleRule, template);
        await test.Db.SaveChangesAsync();
        var sut = new ComplexValidationPlanReader(test.Db, test.TenantAccessor);

        IReadOnlyList<ComplexValidationPlanItem> result = await sut.ReadAsync(templateVersionId);

        ComplexValidationPlanItem item = result.ShouldHaveSingleItem();
        item.RuleVersionId.ShouldBe(complexRule.CurrentPublishedVersionId.Value);
        item.StableRuleCode.ShouldBe("DIMR-DWG");
        item.ExecutorKey.ShouldBe("json-path-compare:v1");
        item.RequiredDocumentCodes.ShouldBe(["DIMR", "DWG"]);
    }

    private static RuleDefinition CreatePublishedRule(
        string stableCode,
        string name,
        RuleCategory category,
        string ast,
        Guid userId,
        DateTime now)
    {
        RuleDefinition rule = RuleDefinition.Create(TenantId, stableCode, name, null, category, ast, "{}", userId, now);
        Guid versionId = rule.Versions.Single().Id;
        rule.SubmitForReview(versionId, rule.Version, now);
        rule.Publish(versionId, rule.Version, true, userId, now);
        return rule;
    }

    private static async Task<TestContext> CreateTestContextAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var tenantAccessor = new TestTenantAccessor();
        DbContextOptions<RulesDbContext> options = new DbContextOptionsBuilder<RulesDbContext>()
            .UseSqlite(connection)
            .Options;
        IHostEnvironment environment = new TestHostEnvironment();
        var db = new RulesDbContext(tenantAccessor, options, Options.Create(new DatabaseOptions()), environment);
        await db.Database.EnsureCreatedAsync();
        return new TestContext(connection, db, tenantAccessor);
    }

    private sealed class TestTenantAccessor : IMultiTenantContextAccessor<AppTenantInfo>
    {
        public IMultiTenantContext<AppTenantInfo> MultiTenantContext { get; } =
            new MultiTenantContext<AppTenantInfo>(new AppTenantInfo(TenantId, TenantId));

        IMultiTenantContext IMultiTenantContextAccessor.MultiTenantContext => MultiTenantContext;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Rules.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class TestContext(
        SqliteConnection connection,
        RulesDbContext db,
        TestTenantAccessor tenantAccessor) : IAsyncDisposable
    {
        internal RulesDbContext Db { get; } = db;
        internal TestTenantAccessor TenantAccessor { get; } = tenantAccessor;

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
