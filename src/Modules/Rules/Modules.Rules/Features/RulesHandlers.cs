using System.Net;
using System.Text.Json;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Persistence;
using DES.Modules.Rules.Runtime;
using Finbuckle.MultiTenant.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Rules.Features;

public sealed class SearchRulesQueryHandler(RulesDbContext db) : IQueryHandler<SearchRulesQuery, PagedResponse<RuleDefinitionSummaryDto>>
{
    public async ValueTask<PagedResponse<RuleDefinitionSummaryDto>> Handle(SearchRulesQuery query, CancellationToken ct)
    {
        int page = Math.Max(query.PageNumber ?? 1, 1); int size = Math.Clamp(query.PageSize ?? 20, 1, 100); IQueryable<RuleDefinition> q = db.RuleDefinitions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search)) { string s = query.Search.Trim(); q = q.Where(x => EF.Functions.ILike(x.Name, $"%{s}%") || EF.Functions.ILike(x.StableCode, $"%{s}%")); }
        if (query.Category is { } category) q = q.Where(x => x.Category == category); if (query.Status is { } status) q = q.Where(x => x.Lifecycle == status);
        RuleCategory[] categories = RuleSearchFilters.ParseCategories(query.Categories); if (categories.Length > 0) q = q.Where(x => categories.Contains(x.Category));
        if (!string.IsNullOrWhiteSpace(query.Severity)) { string severity = JsonSerializer.Serialize(new { severity = query.Severity.Trim() }); q = q.Where(x => x.Versions.OrderByDescending(v => v.VersionNumber).Take(1).Any(v => EF.Functions.JsonContains(v.OutcomeJson, severity))); }
        if (!string.IsNullOrWhiteSpace(query.Tag)) { string tag = JsonSerializer.Serialize(new { tags = new[] { query.Tag.Trim() } }); q = q.Where(x => x.Versions.OrderByDescending(v => v.VersionNumber).Take(1).Any(v => EF.Functions.JsonContains(v.OutcomeJson, tag))); }
        q = query.Sort?.ToUpperInvariant() switch { "-NAME" => q.OrderByDescending(x => x.Name), "CREATED" => q.OrderBy(x => x.CreatedAtUtc), "-CREATED" => q.OrderByDescending(x => x.CreatedAtUtc), "UPDATED" => q.OrderBy(x => x.UpdatedAtUtc), "-UPDATED" => q.OrderByDescending(x => x.UpdatedAtUtc), _ => q.OrderBy(x => x.Name) };
        long total = await q.LongCountAsync(ct).ConfigureAwait(false);
        var rows = await q.Skip((page - 1) * size).Take(size)
            .Select(x => new { Rule = x, Latest = x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new { v.AstJson, v.OutcomeJson }).FirstOrDefault() })
            .ToListAsync(ct).ConfigureAwait(false);
        return new() { Items = rows.Select(r => Map.Summary(r.Rule, r.Latest?.OutcomeJson, RuleAstMetrics.CountConditions(r.Latest?.AstJson))).ToList(), PageNumber = page, PageSize = size, TotalCount = total, TotalPages = (int)Math.Ceiling(total / (double)size) };
    }
}
internal static class RuleSearchFilters
{
    internal static readonly string[] Severities = ["Error", "Warning", "Info"];
    internal static RuleCategory[] ParseCategories(string? value) => string.IsNullOrWhiteSpace(value)
        ? []
        : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => Enum.Parse<RuleCategory>(x, true)).Distinct().ToArray();
    internal static bool IsValidCategoryList(string? value) => string.IsNullOrWhiteSpace(value)
        || value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(x => Enum.TryParse<RuleCategory>(x, true, out RuleCategory parsed) && Enum.IsDefined(parsed) && !int.TryParse(x, out _));
}
public sealed class GetRuleQueryHandler(RulesDbContext db) : IQueryHandler<GetRuleQuery, RuleDefinitionDetailDto>
{
    public async ValueTask<RuleDefinitionDetailDto> Handle(GetRuleQuery query, CancellationToken ct) => Map.Detail(await Load(db, query.DefinitionId, false, ct).ConfigureAwait(false));
    internal static async Task<RuleDefinition> Load(RulesDbContext db, Guid id, bool tracking, CancellationToken ct) { IQueryable<RuleDefinition> q = db.RuleDefinitions.Include(x => x.Versions); if (!tracking) q = q.AsNoTracking(); return await q.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw new CustomException("Rule was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound); }
}
public sealed class CreateRuleCommandHandler(RulesDbContext db, ICurrentUser user, IMultiTenantContextAccessor<AppTenantInfo> tenant) : ICommandHandler<CreateRuleCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateRuleCommand command, CancellationToken ct) { string tenantId = tenant.MultiTenantContext.TenantInfo?.Id ?? throw new CustomException("Tenant is required.", (IEnumerable<string>?)null, HttpStatusCode.Unauthorized); Guid userId = user.GetUserId(); RuleDefinition rule = RuleDefinition.Create(tenantId, command.StableCode, command.Name, command.Description, command.Category, command.AstJson, command.OutcomeJson, userId, DateTime.UtcNow); db.RuleDefinitions.Add(rule); await Save(db, ct).ConfigureAwait(false); return rule.Id; }
    internal static async Task Save(RulesDbContext db, CancellationToken ct) { try { await db.SaveChangesAsync(ct).ConfigureAwait(false); } catch (DbUpdateConcurrencyException) { throw new CustomException("The rule was modified by another request.", (IEnumerable<string>?)null, HttpStatusCode.Conflict); } catch (DbUpdateException) { throw new CustomException("The rule conflicts with an existing rule.", (IEnumerable<string>?)null, HttpStatusCode.Conflict); } }
}
public sealed class UpdateRuleDraftCommandHandler(RulesDbContext db) : ICommandHandler<UpdateRuleDraftCommand>
{
    public async ValueTask<Unit> Handle(UpdateRuleDraftCommand c, CancellationToken ct) { RuleDefinition r = await GetRuleQueryHandler.Load(db, c.DefinitionId, true, ct).ConfigureAwait(false); r.UpdateDraft(c.VersionId, c.ExpectedAggregateVersion, c.Name, c.Description, c.Category, c.AstJson, c.OutcomeJson, DateTime.UtcNow); await CreateRuleCommandHandler.Save(db, ct).ConfigureAwait(false); return Unit.Value; }
}
public sealed class SubmitRuleForReviewCommandHandler(RulesDbContext db) : ICommandHandler<SubmitRuleForReviewCommand>
{
    public async ValueTask<Unit> Handle(SubmitRuleForReviewCommand c, CancellationToken ct) { RuleDefinition r = await GetRuleQueryHandler.Load(db, c.DefinitionId, true, ct).ConfigureAwait(false); r.SubmitForReview(c.VersionId, c.ExpectedAggregateVersion, DateTime.UtcNow); await CreateRuleCommandHandler.Save(db, ct).ConfigureAwait(false); return Unit.Value; }
}
public sealed class PublishRuleCommandHandler(RulesDbContext db, ICurrentUser user) : ICommandHandler<PublishRuleCommand>
{
    public async ValueTask<Unit> Handle(PublishRuleCommand c, CancellationToken ct) { RuleDefinition r = await GetRuleQueryHandler.Load(db, c.DefinitionId, true, ct).ConfigureAwait(false); r.Publish(c.VersionId, c.ExpectedAggregateVersion, c.RetireCurrent, user.GetUserId(), DateTime.UtcNow); await CreateRuleCommandHandler.Save(db, ct).ConfigureAwait(false); return Unit.Value; }
}
public sealed class RetireRuleCommandHandler(RulesDbContext db) : ICommandHandler<RetireRuleCommand>
{
    public async ValueTask<Unit> Handle(RetireRuleCommand c, CancellationToken ct) { RuleDefinition r = await GetRuleQueryHandler.Load(db, c.DefinitionId, true, ct).ConfigureAwait(false); r.Retire(c.VersionId, c.ExpectedAggregateVersion, DateTime.UtcNow); await CreateRuleCommandHandler.Save(db, ct).ConfigureAwait(false); return Unit.Value; }
}
public sealed class ClonePublishedRuleCommandHandler(RulesDbContext db, ICurrentUser user) : ICommandHandler<ClonePublishedRuleCommand, Guid>
{
    public async ValueTask<Guid> Handle(ClonePublishedRuleCommand c, CancellationToken ct) { RuleDefinition r = await GetRuleQueryHandler.Load(db, c.DefinitionId, true, ct).ConfigureAwait(false); RuleVersion v = r.ClonePublished(c.VersionId, c.ExpectedAggregateVersion, user.GetUserId(), DateTime.UtcNow); await CreateRuleCommandHandler.Save(db, ct).ConfigureAwait(false); return v.Id; }
}
public sealed class DeleteRuleCommandHandler(RulesDbContext db) : ICommandHandler<DeleteRuleCommand>
{
    public async ValueTask<Unit> Handle(DeleteRuleCommand c, CancellationToken ct)
    {
        RuleDefinition r = await GetRuleQueryHandler.Load(db, c.DefinitionId, true, ct).ConfigureAwait(false);
        r.EnsureDeletable(c.ExpectedAggregateVersion);
        Guid[] versionIds = r.Versions.Select(v => v.Id).ToArray();
        if (await db.TemplateRuleAssignments.AnyAsync(x => versionIds.Contains(x.RuleVersionId), ct).ConfigureAwait(false))
            throw new CustomException("The rule is assigned to a rule template version and cannot be deleted.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        if (await db.RuleEvaluationResults.AnyAsync(x => x.RuleDefinitionId == r.Id, ct).ConfigureAwait(false))
            throw new CustomException("The rule has evaluation history and cannot be deleted.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        db.RuleDefinitions.Remove(r);
        await CreateRuleCommandHandler.Save(db, ct).ConfigureAwait(false);
        return Unit.Value;
    }
}
public sealed class TestRuleCommandHandler : ICommandHandler<TestRuleCommand, RuleEvaluationResultDto>
{
    public ValueTask<RuleEvaluationResultDto> Handle(TestRuleCommand c, CancellationToken ct) { ct.ThrowIfCancellationRequested(); var docs = new Dictionary<string, JsonElement>(StringComparer.Ordinal); foreach ((string code, string json) in c.Documents) { using JsonDocument d = JsonDocument.Parse(json); docs.Add(code, d.RootElement.Clone()); } IReadOnlyDictionary<string, JsonElement> parameters = RuleParameterContext.Parse(c.ParametersJson); RuleEvaluationResult result = new RuleEvaluator().Evaluate(RuleAstSerializer.Parse(c.AstJson), new(docs, parameters)); return ValueTask.FromResult(new RuleEvaluationResultDto(result.Status, result.Reason, result.Trace.Select(x => new RuleTraceDto(x.Node, x.Operator, x.Left, x.Right, x.Status, x.Reason)).ToArray())); }
}
internal static class Map
{
    internal static RuleDefinitionSummaryDto Summary(RuleDefinition x, string? latestOutcomeJson, int conditionCount) => new(x.Id, x.StableCode, x.Name, x.Description, x.Category, x.Lifecycle, x.CurrentPublishedVersionId, x.CreatedAtUtc, x.UpdatedAtUtc, x.Version, latestOutcomeJson, conditionCount);
    internal static RuleDefinitionDetailDto Detail(RuleDefinition x) => new(x.Id, x.StableCode, x.Name, x.Description, x.Category, x.Lifecycle, x.CurrentPublishedVersionId, x.CreatedAtUtc, x.UpdatedAtUtc, x.Version, x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new RuleVersionMetadataDto(v.Id, v.VersionNumber, v.Status, v.CreatedAtUtc, v.PublishedAtUtc, v.RetiredAtUtc, v.CreatedByUserId, v.AstJson, v.OutcomeJson)).ToArray());
}
