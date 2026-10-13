using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Persistence;
using Finbuckle.MultiTenant.Abstractions;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Rules.Features;

public sealed class SearchTemplatesQueryHandler(RulesDbContext db) : IQueryHandler<SearchTemplatesQuery, PagedResponse<RuleTemplateSummaryDto>>
{
    public async ValueTask<PagedResponse<RuleTemplateSummaryDto>> Handle(SearchTemplatesQuery query, CancellationToken ct)
    {
        int page = Math.Max(query.PageNumber ?? 1, 1);
        int size = Math.Clamp(query.PageSize ?? 20, 1, 100);
        IQueryable<RuleTemplate> templates = db.RuleTemplates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string search = query.Search.Trim();
            templates = templates.Where(x => EF.Functions.ILike(x.Name, $"%{search}%") || EF.Functions.ILike(x.StableCode, $"%{search}%"));
        }
        if (query.Status is { } status) templates = templates.Where(x => x.Lifecycle == status);
        templates = query.Sort?.ToUpperInvariant() switch
        {
            "-NAME" => templates.OrderByDescending(x => x.Name),
            "CREATED" => templates.OrderBy(x => x.CreatedAtUtc),
            "-CREATED" => templates.OrderByDescending(x => x.CreatedAtUtc),
            _ => templates.OrderBy(x => x.Name),
        };
        long total = await templates.LongCountAsync(ct).ConfigureAwait(false);
        List<RuleTemplate> rows = await templates.Skip((page - 1) * size).Take(size).ToListAsync(ct).ConfigureAwait(false);
        return new() { Items = rows.Select(TemplateMap.Summary).ToList(), PageNumber = page, PageSize = size, TotalCount = total, TotalPages = (int)Math.Ceiling(total / (double)size) };
    }
}

public sealed class GetTemplateQueryHandler(RulesDbContext db) : IQueryHandler<GetTemplateQuery, RuleTemplateDetailDto>
{
    public async ValueTask<RuleTemplateDetailDto> Handle(GetTemplateQuery query, CancellationToken ct) => TemplateMap.Detail(await Load(db, query.TemplateId, false, ct).ConfigureAwait(false));

    internal static async Task<RuleTemplate> Load(RulesDbContext db, Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<RuleTemplate> templates = db.RuleTemplates
            .Include(x => x.Versions).ThenInclude(x => x.DocumentRequirements)
            .Include(x => x.Versions).ThenInclude(x => x.RuleAssignments);
        if (!tracking) templates = templates.AsNoTracking();
        return await templates.AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
            ?? throw new CustomException("Template was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);
    }
}

public sealed class GetTemplateVersionQueryHandler(RulesDbContext db) : IQueryHandler<GetTemplateVersionQuery, RuleTemplateVersionDto>
{
    public async ValueTask<RuleTemplateVersionDto> Handle(GetTemplateVersionQuery query, CancellationToken ct)
    {
        RuleTemplate template = await GetTemplateQueryHandler.Load(db, query.TemplateId, false, ct).ConfigureAwait(false);
        RuleTemplateVersion version = template.Versions.SingleOrDefault(x => x.Id == query.VersionId)
            ?? throw new CustomException("Template version was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);
        if (version.Status is not (RuleLifecycle.Published or RuleLifecycle.Retired))
        {
            throw new CustomException("Only published or retired template versions can be used as snapshots.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        }
        return TemplateMap.Version(version);
    }
}

public sealed class CreateTemplateCommandHandler(RulesDbContext db, ICurrentUser user, IMultiTenantContextAccessor<AppTenantInfo> tenant) : ICommandHandler<CreateTemplateCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateTemplateCommand command, CancellationToken ct)
    {
        string tenantId = tenant.MultiTenantContext.TenantInfo?.Id ?? throw new CustomException("Tenant is required.", (IEnumerable<string>?)null, HttpStatusCode.Unauthorized);
        RuleTemplate template = RuleTemplate.Create(tenantId, command.StableCode, command.Name, command.Description, user.GetUserId(), DateTime.UtcNow);
        db.RuleTemplates.Add(template);
        await Save(db, ct).ConfigureAwait(false);
        return template.Id;
    }

    internal static async Task Save(RulesDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException) { throw new CustomException("The template was modified by another request.", (IEnumerable<string>?)null, HttpStatusCode.Conflict); }
        catch (DbUpdateException) { throw new CustomException("The template conflicts with existing data.", (IEnumerable<string>?)null, HttpStatusCode.Conflict); }
    }
}

public sealed class ConfigureTemplateDraftCommandHandler(RulesDbContext db) : ICommandHandler<ConfigureTemplateDraftCommand>
{
    public async ValueTask<Unit> Handle(ConfigureTemplateDraftCommand command, CancellationToken ct)
    {
        RuleTemplate template = await GetTemplateQueryHandler.Load(db, command.TemplateId, true, ct).ConfigureAwait(false);
        Guid[] referencedVersions = command.RuleAssignments.Select(x => x.RuleVersionId).Distinct().ToArray();
        int existingCount = await db.RuleVersions.CountAsync(x => referencedVersions.Contains(x.Id), ct).ConfigureAwait(false);
        if (existingCount != referencedVersions.Length) throw new CustomException("Every assigned rule version must exist in the same tenant.", (IEnumerable<string>?)null, HttpStatusCode.BadRequest);
        template.ConfigureDraft(command.VersionId, command.ExpectedAggregateVersion, command.Name, command.Description, command.DocumentRequirements, command.RuleAssignments, DateTime.UtcNow);
        await CreateTemplateCommandHandler.Save(db, ct).ConfigureAwait(false);
        return Unit.Value;
    }
}

public sealed class SubmitTemplateForReviewCommandHandler(RulesDbContext db) : ICommandHandler<SubmitTemplateForReviewCommand>
{
    public async ValueTask<Unit> Handle(SubmitTemplateForReviewCommand command, CancellationToken ct)
    {
        RuleTemplate template = await GetTemplateQueryHandler.Load(db, command.TemplateId, true, ct).ConfigureAwait(false);
        template.SubmitForReview(command.VersionId, command.ExpectedAggregateVersion, DateTime.UtcNow);
        await CreateTemplateCommandHandler.Save(db, ct).ConfigureAwait(false);
        return Unit.Value;
    }
}

public sealed class PublishTemplateCommandHandler(RulesDbContext db, ICurrentUser user) : ICommandHandler<PublishTemplateCommand>
{
    public async ValueTask<Unit> Handle(PublishTemplateCommand command, CancellationToken ct)
    {
        RuleTemplate template = await GetTemplateQueryHandler.Load(db, command.TemplateId, true, ct).ConfigureAwait(false);
        RuleTemplateVersion version = template.Versions.SingleOrDefault(x => x.Id == command.VersionId)
            ?? throw new CustomException("Template version was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);
        Guid[] referencedVersions = version.RuleAssignments.Select(x => x.RuleVersionId).Distinct().ToArray();
        int publishedCount = await db.RuleVersions.CountAsync(x => referencedVersions.Contains(x.Id) && x.Status == RuleLifecycle.Published, ct).ConfigureAwait(false);
        if (publishedCount != referencedVersions.Length) throw new CustomException("Every assigned rule version must exist in the same tenant and be published.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        template.Publish(command.VersionId, command.ExpectedAggregateVersion, command.RetireCurrent, user.GetUserId(), DateTime.UtcNow);
        await CreateTemplateCommandHandler.Save(db, ct).ConfigureAwait(false);
        return Unit.Value;
    }
}

public sealed class RetireTemplateCommandHandler(RulesDbContext db) : ICommandHandler<RetireTemplateCommand>
{
    public async ValueTask<Unit> Handle(RetireTemplateCommand command, CancellationToken ct)
    {
        RuleTemplate template = await GetTemplateQueryHandler.Load(db, command.TemplateId, true, ct).ConfigureAwait(false);
        template.Retire(command.VersionId, command.ExpectedAggregateVersion, DateTime.UtcNow);
        await CreateTemplateCommandHandler.Save(db, ct).ConfigureAwait(false);
        return Unit.Value;
    }
}

public sealed class ClonePublishedTemplateCommandHandler(RulesDbContext db, ICurrentUser user) : ICommandHandler<ClonePublishedTemplateCommand, Guid>
{
    public async ValueTask<Guid> Handle(ClonePublishedTemplateCommand command, CancellationToken ct)
    {
        RuleTemplate template = await GetTemplateQueryHandler.Load(db, command.TemplateId, true, ct).ConfigureAwait(false);
        RuleTemplateVersion clone = template.ClonePublished(command.VersionId, command.ExpectedAggregateVersion, user.GetUserId(), DateTime.UtcNow);
        await CreateTemplateCommandHandler.Save(db, ct).ConfigureAwait(false);
        return clone.Id;
    }
}

internal static class TemplateMap
{
    internal static RuleTemplateSummaryDto Summary(RuleTemplate template) => new(template.Id, template.StableCode, template.Name, template.Description, template.Lifecycle, template.CurrentPublishedVersionId, template.CreatedAtUtc, template.UpdatedAtUtc, template.Version);

    internal static RuleTemplateVersionDto Version(RuleTemplateVersion version) => new(
        version.Id,
        version.VersionNumber,
        version.Status,
        version.CreatedByUserId,
        version.CreatedAtUtc,
        version.PublishedAtUtc,
        version.RetiredAtUtc,
        version.DocumentRequirements.OrderBy(r => r.SortOrder).ThenBy(r => r.DocumentCode).Select(r => new TemplateDocumentRequirementDto(r.Id, r.DocumentCode, r.IsRequired, r.SortOrder, r.Disposition)).ToArray(),
        version.RuleAssignments.OrderBy(a => a.DocumentCode).ThenBy(a => a.RuleVersionId).Select(a => new TemplateRuleAssignmentDto(a.Id, a.RuleVersionId, a.DocumentCode, a.ParametersJson)).ToArray());

    internal static RuleTemplateDetailDto Detail(RuleTemplate template) => new(
        template.Id,
        template.StableCode,
        template.Name,
        template.Description,
        template.Lifecycle,
        template.CurrentPublishedVersionId,
        template.CreatedAtUtc,
        template.UpdatedAtUtc,
        template.Version,
        template.Versions.OrderByDescending(x => x.VersionNumber).Select(Version).ToArray());
}
