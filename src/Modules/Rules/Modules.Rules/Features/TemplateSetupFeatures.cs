using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Framework.Web.Idempotency;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Contracts.Authorization;
using DES.Modules.Rules.Domain;
using DES.Modules.Rules.Persistence;
using Finbuckle.MultiTenant.Abstractions;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Rules.Features;

public sealed record UpdateDocumentSettingRequest(long ExpectedVersion, string Name, bool IsEnabled, int SortOrder, TemplateDocumentDisposition DefaultDisposition, IReadOnlyList<Guid> RuleDefinitionIds);
public sealed record SaveTemplateSetupRequest(long ExpectedVersion, string Name, string? Description, IReadOnlyList<TemplateSetupDocumentInput> Documents, IReadOnlyList<TemplateSetupRuleInput> Rules);

internal static class TemplateSetupEndpoints
{
    internal static void MapTemplateSetup(this IEndpointRouteBuilder e)
    {
        e.MapGet("/document-settings", (IMediator m, CancellationToken ct) => m.Send(new GetDocumentSettingsQuery(), ct)).RequirePermission(RulesPermissions.DocumentSettings.View);
        e.MapPut("/document-settings/{id:guid}", (Guid id, UpdateDocumentSettingRequest r, IMediator m, CancellationToken ct) => m.Send(new UpdateDocumentSettingCommand(id, r.ExpectedVersion, r.Name, r.IsEnabled, r.SortOrder, r.DefaultDisposition, r.RuleDefinitionIds), ct)).RequirePermission(RulesPermissions.DocumentSettings.Edit);
        e.MapGet("/definitions/lookup", (IMediator m, CancellationToken ct) => m.Send(new GetRuleLookupQuery(), ct)).RequirePermission(RulesPermissions.Definitions.View);
        e.MapGet("/template-setups", ([AsParameters] SearchTemplateSetupsQuery q, IMediator m, CancellationToken ct) => m.Send(q, ct)).RequirePermission(RulesPermissions.Templates.View);
        e.MapGet("/template-setups/{id:guid}", (Guid id, IMediator m, CancellationToken ct) => m.Send(new GetTemplateSetupQuery(id), ct)).RequirePermission(RulesPermissions.Templates.View);
        e.MapPost("/template-setups", (CreateTemplateFromDocumentSettingsCommand c, IMediator m, CancellationToken ct) => m.Send(c, ct)).RequirePermission(RulesPermissions.Templates.Create).WithIdempotency();
        e.MapPut("/template-setups/{id:guid}", (Guid id, SaveTemplateSetupRequest r, IMediator m, CancellationToken ct) => m.Send(new SaveTemplateSetupCommand(id, r.ExpectedVersion, r.Name, r.Description, r.Documents, r.Rules), ct)).RequirePermission(RulesPermissions.Templates.Edit);
    }
}

public sealed class GetDocumentSettingsQueryHandler(RulesDbContext db) : IQueryHandler<GetDocumentSettingsQuery, IReadOnlyList<DocumentSettingDto>>
{
    public async ValueTask<IReadOnlyList<DocumentSettingDto>> Handle(GetDocumentSettingsQuery query, CancellationToken ct)
    {
        List<DocumentSetting> settings = await db.DocumentSettings.AsNoTracking().Include(x => x.Rules).OrderBy(x => x.SortOrder).ThenBy(x => x.DocumentCode).ToListAsync(ct).ConfigureAwait(false);
        return settings.Select(SetupMap.Setting).ToList();
    }
}

public sealed class UpdateDocumentSettingCommandHandler(RulesDbContext db) : ICommandHandler<UpdateDocumentSettingCommand, DocumentSettingDto>
{
    public async ValueTask<DocumentSettingDto> Handle(UpdateDocumentSettingCommand c, CancellationToken ct)
    {
        DocumentSetting setting = await db.DocumentSettings.Include(x => x.Rules).SingleOrDefaultAsync(x => x.Id == c.Id, ct).ConfigureAwait(false)
            ?? throw new CustomException("Document setting was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);
        await SetupMap.EnsureRulesExist(db, c.RuleDefinitionIds, ct).ConfigureAwait(false);
        setting.Update(c.ExpectedVersion, c.Name, c.IsEnabled, c.SortOrder, c.DefaultDisposition, c.RuleDefinitionIds, DateTime.UtcNow);
        await SetupMap.Save(db, "document setting", ct).ConfigureAwait(false);
        return SetupMap.Setting(setting);
    }
}

public sealed class GetRuleLookupQueryHandler(RulesDbContext db) : IQueryHandler<GetRuleLookupQuery, IReadOnlyList<RuleLookupDto>>
{
    public async ValueTask<IReadOnlyList<RuleLookupDto>> Handle(GetRuleLookupQuery query, CancellationToken ct) =>
        await db.RuleDefinitions.AsNoTracking().Where(x => x.ArchivedAtUtc == null).OrderBy(x => x.StableCode).Take(5000)
            .Select(x => new RuleLookupDto(x.Id, x.StableCode, x.Name, x.Description, x.Category, x.Lifecycle, x.CurrentPublishedVersionId != null,
                x.Versions.OrderByDescending(v => v.VersionNumber).Select(v => v.OutcomeJson).FirstOrDefault()))
            .ToListAsync(ct).ConfigureAwait(false);
}

public sealed class SearchTemplateSetupsQueryHandler(RulesDbContext db) : IQueryHandler<SearchTemplateSetupsQuery, PagedResponse<TemplateSetupSummaryDto>>
{
    public async ValueTask<PagedResponse<TemplateSetupSummaryDto>> Handle(SearchTemplateSetupsQuery query, CancellationToken ct)
    {
        int page = Math.Max(query.PageNumber ?? 1, 1); int size = Math.Clamp(query.PageSize ?? 50, 1, 100);
        IQueryable<RuleTemplate> q = db.RuleTemplates.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search)) { string s = query.Search.Trim(); q = q.Where(x => EF.Functions.ILike(x.Name, $"%{s}%") || EF.Functions.ILike(x.StableCode, $"%{s}%")); }
        q = query.Sort?.ToUpperInvariant() switch { "-NAME" => q.OrderByDescending(x => x.Name), "-UPDATED" => q.OrderByDescending(x => x.UpdatedAtUtc), _ => q.OrderBy(x => x.Name) };
        long total = await q.LongCountAsync(ct).ConfigureAwait(false);
        var rows = await q.Skip((page - 1) * size).Take(size).Select(x => new
        {
            x.Id, x.StableCode, x.Name, x.Description, x.Lifecycle, x.CurrentPublishedVersionId, x.UpdatedAtUtc, x.Version,
            Documents = x.Documents.Count,
            EnabledDocuments = x.Documents.Count(d => d.IsEnabled),
            Rules = x.Rules.Count,
            EnabledRules = x.Rules.Count(r => r.IsEnabled && x.Documents.Any(d => d.IsEnabled && d.DocumentCode == r.DocumentCode)),
            PublishedDocuments = x.Versions.Where(v => v.Id == x.CurrentPublishedVersionId).SelectMany(v => v.DocumentRequirements).Count(),
            PublishedRules = x.Versions.Where(v => v.Id == x.CurrentPublishedVersionId).SelectMany(v => v.RuleAssignments).Count(),
        }).ToListAsync(ct).ConfigureAwait(false);
        List<TemplateSetupSummaryDto> items = rows.Select(r => r.Documents > 0
            ? new TemplateSetupSummaryDto(r.Id, r.StableCode, r.Name, r.Description, r.Lifecycle, r.CurrentPublishedVersionId, r.EnabledDocuments, r.Documents, r.EnabledRules, r.Rules, r.UpdatedAtUtc, r.Version)
            : new TemplateSetupSummaryDto(r.Id, r.StableCode, r.Name, r.Description, r.Lifecycle, r.CurrentPublishedVersionId, r.PublishedDocuments, r.PublishedDocuments, r.PublishedRules, r.PublishedRules, r.UpdatedAtUtc, r.Version)).ToList();
        return new() { Items = items, PageNumber = page, PageSize = size, TotalCount = total, TotalPages = (int)Math.Ceiling(total / (double)size) };
    }
}

public sealed class GetTemplateSetupQueryHandler(RulesDbContext db) : IQueryHandler<GetTemplateSetupQuery, TemplateSetupDto>
{
    public async ValueTask<TemplateSetupDto> Handle(GetTemplateSetupQuery query, CancellationToken ct)
    {
        RuleTemplate template = await SetupMap.Load(db, query.TemplateId, false, ct).ConfigureAwait(false);
        if (template.Documents.Count > 0) return SetupMap.Setup(template, template.Documents.Select(SetupMap.Document).ToList(), template.Rules.Select(SetupMap.Rule).ToList());

        RuleTemplateVersion? published = template.Versions.SingleOrDefault(x => x.Id == template.CurrentPublishedVersionId);
        List<TemplateSetupDocumentDto> documents = published?.DocumentRequirements.Select(x => new TemplateSetupDocumentDto(x.DocumentCode, true, x.Disposition, x.SortOrder)).ToList() ?? [];
        Guid[] versionIds = published?.RuleAssignments.Where(x => x.DocumentCode is not null).Select(x => x.RuleVersionId).Distinct().ToArray() ?? [];
        Dictionary<Guid, Guid> definitionByVersion = await db.RuleVersions.AsNoTracking().Where(x => versionIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, x => x.RuleDefinitionId, ct).ConfigureAwait(false);
        List<TemplateSetupRuleDto> rules = published?.RuleAssignments
            .Where(x => x.DocumentCode is not null && definitionByVersion.ContainsKey(x.RuleVersionId))
            .Select(x => new TemplateSetupRuleDto(x.DocumentCode!, definitionByVersion[x.RuleVersionId], true))
            .DistinctBy(x => (x.DocumentCode, x.RuleDefinitionId)).ToList() ?? [];
        HashSet<string> present = documents.Select(x => x.DocumentCode).ToHashSet(StringComparer.Ordinal);
        List<DocumentSetting> settings = await db.DocumentSettings.AsNoTracking().OrderBy(x => x.SortOrder).ToListAsync(ct).ConfigureAwait(false);
        documents.AddRange(settings.Where(x => !present.Contains(x.DocumentCode)).Select(x => new TemplateSetupDocumentDto(x.DocumentCode, false, x.DefaultDisposition, x.SortOrder)));
        return SetupMap.Setup(template, documents, rules);
    }
}

public sealed class CreateTemplateFromDocumentSettingsCommandHandler(RulesDbContext db, ICurrentUser user, IMultiTenantContextAccessor<AppTenantInfo> tenant) : ICommandHandler<CreateTemplateFromDocumentSettingsCommand, TemplateSetupSaveResultDto>
{
    public async ValueTask<TemplateSetupSaveResultDto> Handle(CreateTemplateFromDocumentSettingsCommand c, CancellationToken ct)
    {
        string tenantId = tenant.MultiTenantContext.TenantInfo?.Id ?? throw new CustomException("Tenant is required.", (IEnumerable<string>?)null, HttpStatusCode.Unauthorized);
        List<DocumentSetting> settings = await db.DocumentSettings.AsNoTracking().Include(x => x.Rules).OrderBy(x => x.SortOrder).ToListAsync(ct).ConfigureAwait(false);
        DateTime now = DateTime.UtcNow;
        RuleTemplate template = RuleTemplate.CreateForSetup(tenantId, c.StableCode, c.Name, c.Description, now);
        template.ConfigureSetup(
            template.Version, c.Name, c.Description,
            settings.Select(x => new TemplateSetupDocumentInput(x.DocumentCode, x.IsEnabled, x.DefaultDisposition, x.SortOrder)),
            settings.SelectMany(x => x.Rules.Select(r => new TemplateSetupRuleInput(x.DocumentCode, r.RuleDefinitionId, true))),
            now);
        (Guid versionId, IReadOnlyList<Guid> skipped) = await SetupMap.Materialize(db, template, user.GetUserId(), ct).ConfigureAwait(false);
        db.RuleTemplates.Add(template);
        await SetupMap.Save(db, "template", ct).ConfigureAwait(false);
        return new TemplateSetupSaveResultDto(template.Id, versionId, template.Version, skipped);
    }
}

public sealed class SaveTemplateSetupCommandHandler(RulesDbContext db, ICurrentUser user) : ICommandHandler<SaveTemplateSetupCommand, TemplateSetupSaveResultDto>
{
    public async ValueTask<TemplateSetupSaveResultDto> Handle(SaveTemplateSetupCommand c, CancellationToken ct)
    {
        RuleTemplate template = await SetupMap.Load(db, c.TemplateId, true, ct).ConfigureAwait(false);
        await SetupMap.EnsureRulesExist(db, c.Rules.Select(x => x.RuleDefinitionId), ct).ConfigureAwait(false);
        template.ConfigureSetup(c.ExpectedVersion, c.Name, c.Description, c.Documents, c.Rules, DateTime.UtcNow);
        (Guid versionId, IReadOnlyList<Guid> skipped) = await SetupMap.Materialize(db, template, user.GetUserId(), ct).ConfigureAwait(false);
        await SetupMap.Save(db, "template", ct).ConfigureAwait(false);
        return new TemplateSetupSaveResultDto(template.Id, versionId, template.Version, skipped);
    }
}

public sealed class UpdateDocumentSettingCommandValidator : AbstractValidator<UpdateDocumentSettingCommand>
{
    public UpdateDocumentSettingCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200); RuleFor(x => x.SortOrder).InclusiveBetween(1, 1000); RuleFor(x => x.DefaultDisposition).IsInEnum();
        RuleFor(x => x.RuleDefinitionIds).NotNull().Must(x => x is { Count: <= DocumentSetting.MaxRules }).WithMessage($"A document can have at most {DocumentSetting.MaxRules} rules.");
    }
}

public sealed class SearchTemplateSetupsQueryValidator : AbstractValidator<SearchTemplateSetupsQuery>
{
    public SearchTemplateSetupsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Sort).Must(x => x is null or "name" or "-name" or "-updated");
    }
}

public sealed class CreateTemplateFromDocumentSettingsCommandValidator : AbstractValidator<CreateTemplateFromDocumentSettingsCommand>
{
    public CreateTemplateFromDocumentSettingsCommandValidator()
    {
        RuleFor(x => x.StableCode).NotEmpty().MaximumLength(64).Matches("^[A-Za-z0-9_.-]+$");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200); RuleFor(x => x.Description).MaximumLength(2000);
    }
}

public sealed class SaveTemplateSetupCommandValidator : AbstractValidator<SaveTemplateSetupCommand>
{
    public SaveTemplateSetupCommandValidator()
    {
        RuleFor(x => x.TemplateId).NotEmpty(); RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200); RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.Documents).NotNull().Must(x => x is { Count: <= 50 }).WithMessage("No more than 50 documents are allowed.")
            .Must(x => x is null || x.Select(d => d.DocumentCode?.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).Count() == x.Count).WithMessage("Document codes must be unique.");
        RuleForEach(x => x.Documents).ChildRules(d =>
        {
            d.RuleFor(x => x.DocumentCode).NotEmpty().MaximumLength(32).Matches("^[A-Za-z0-9_.-]+$");
            d.RuleFor(x => x.Disposition).IsInEnum(); d.RuleFor(x => x.SortOrder).InclusiveBetween(1, 1000);
        });
        RuleFor(x => x.Rules).NotNull().Must(x => x is { Count: <= 10000 }).WithMessage("No more than 10000 template rules are allowed.")
            .Must(x => x is null || x.Select(r => (r.DocumentCode?.Trim().ToUpperInvariant(), r.RuleDefinitionId)).Distinct().Count() == x.Count).WithMessage("A rule can be added to a document only once.");
        RuleForEach(x => x.Rules).ChildRules(r => { r.RuleFor(x => x.DocumentCode).NotEmpty().MaximumLength(32); r.RuleFor(x => x.RuleDefinitionId).NotEmpty(); });
    }
}

internal static class SetupMap
{
    internal static DocumentSettingDto Setting(DocumentSetting x) => new(x.Id, x.DocumentCode, x.Name, x.IsEnabled, x.SortOrder, x.DefaultDisposition, x.Rules.Select(r => r.RuleDefinitionId).ToList(), x.UpdatedAtUtc, x.Version);
    internal static TemplateSetupDocumentDto Document(RuleTemplateDocument x) => new(x.DocumentCode, x.IsEnabled, x.Disposition, x.SortOrder);
    internal static TemplateSetupRuleDto Rule(RuleTemplateRule x) => new(x.DocumentCode, x.RuleDefinitionId, x.IsEnabled);

    internal static TemplateSetupDto Setup(RuleTemplate t, List<TemplateSetupDocumentDto> documents, List<TemplateSetupRuleDto> rules) => new(
        t.Id, t.StableCode, t.Name, t.Description, t.Lifecycle, t.CurrentPublishedVersionId, t.UpdatedAtUtc, t.Version,
        documents.OrderBy(x => x.SortOrder).ThenBy(x => x.DocumentCode, StringComparer.Ordinal).ToList(), rules);

    internal static async Task<RuleTemplate> Load(RulesDbContext db, Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<RuleTemplate> q = db.RuleTemplates
            .Include(x => x.Versions).ThenInclude(x => x.DocumentRequirements)
            .Include(x => x.Versions).ThenInclude(x => x.RuleAssignments)
            .Include(x => x.Documents).Include(x => x.Rules);
        if (!tracking) q = q.AsNoTracking();
        return await q.AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false)
            ?? throw new CustomException("Template was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);
    }

    internal static async Task EnsureRulesExist(RulesDbContext db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        Guid[] distinct = ids.Distinct().ToArray();
        int found = await db.RuleDefinitions.CountAsync(x => distinct.Contains(x.Id), ct).ConfigureAwait(false);
        if (found != distinct.Length) throw new CustomException("One or more rules were not found.", (IEnumerable<string>?)null, HttpStatusCode.BadRequest);
    }

    internal static async Task<(Guid VersionId, IReadOnlyList<Guid> Skipped)> Materialize(RulesDbContext db, RuleTemplate template, Guid userId, CancellationToken ct)
    {
        HashSet<string> enabledCodes = template.Documents.Where(x => x.IsEnabled).Select(x => x.DocumentCode).ToHashSet(StringComparer.Ordinal);
        List<RuleTemplateRule> enabledRules = template.Rules.Where(x => x.IsEnabled && enabledCodes.Contains(x.DocumentCode)).ToList();
        Guid[] definitionIds = enabledRules.Select(x => x.RuleDefinitionId).Distinct().ToArray();
        Dictionary<Guid, Guid?> live = await db.RuleDefinitions.AsNoTracking().Where(x => definitionIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.ArchivedAtUtc == null ? x.CurrentPublishedVersionId : null, ct).ConfigureAwait(false);
        List<TemplateDocumentRequirementInput> requirements = template.Documents.Where(x => x.IsEnabled)
            .Select(x => new TemplateDocumentRequirementInput(x.DocumentCode, x.Disposition != TemplateDocumentDisposition.Retain, x.SortOrder, x.Disposition)).ToList();
        List<TemplateRuleAssignmentInput> assignments = enabledRules
            .Where(x => live.GetValueOrDefault(x.RuleDefinitionId) is not null)
            .Select(x => new TemplateRuleAssignmentInput(live[x.RuleDefinitionId]!.Value, x.DocumentCode, "{}")).ToList();
        Guid[] skipped = enabledRules.Where(x => live.GetValueOrDefault(x.RuleDefinitionId) is null).Select(x => x.RuleDefinitionId).Distinct().ToArray();
        RuleTemplateVersion version = template.PublishSetup(requirements, assignments, userId, DateTime.UtcNow);
        return (version.Id, skipped);
    }

    internal static async Task Save(RulesDbContext db, string what, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException) { throw new CustomException($"The {what} was modified by another request. Reload and try again.", (IEnumerable<string>?)null, HttpStatusCode.Conflict); }
        catch (DbUpdateException) { throw new CustomException($"The {what} conflicts with existing data (is the code already used?).", (IEnumerable<string>?)null, HttpStatusCode.Conflict); }
    }
}
