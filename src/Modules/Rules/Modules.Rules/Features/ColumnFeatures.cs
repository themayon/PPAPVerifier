using System.Net;
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

public sealed record UpdateRuleColumnRequest(long ExpectedVersion, string Name, string Key, RuleColumnDataType DataType, string? HeaderNames, string? Description, bool IsActive);

internal static class ColumnEndpoints
{
    internal static void MapColumns(this IEndpointRouteBuilder e)
    {
        e.MapGet("/columns", ([AsParameters] SearchRuleColumnsQuery q, IMediator m, CancellationToken ct) => m.Send(q, ct)).RequirePermission(RulesPermissions.Columns.View);
        e.MapGet("/columns/{id:guid}", (Guid id, IMediator m, CancellationToken ct) => m.Send(new GetRuleColumnQuery(id), ct)).RequirePermission(RulesPermissions.Columns.View);
        e.MapPost("/columns", (CreateRuleColumnCommand c, IMediator m, CancellationToken ct) => m.Send(c, ct)).RequirePermission(RulesPermissions.Columns.Create).WithIdempotency();
        e.MapPut("/columns/{id:guid}", (Guid id, UpdateRuleColumnRequest r, IMediator m, CancellationToken ct) => m.Send(new UpdateRuleColumnCommand(id, r.ExpectedVersion, r.Name, r.Key, r.DataType, r.HeaderNames, r.Description, r.IsActive), ct)).RequirePermission(RulesPermissions.Columns.Edit);
        e.MapDelete("/columns/{id:guid}", (Guid id, IMediator m, CancellationToken ct) => m.Send(new DeleteRuleColumnCommand(id), ct)).RequirePermission(RulesPermissions.Columns.Delete);
    }
}

public sealed class SearchRuleColumnsQueryHandler(RulesDbContext db) : IQueryHandler<SearchRuleColumnsQuery, PagedResponse<RuleColumnDto>>
{
    public async ValueTask<PagedResponse<RuleColumnDto>> Handle(SearchRuleColumnsQuery query, CancellationToken ct)
    {
        int page = Math.Max(query.PageNumber ?? 1, 1); int size = Math.Clamp(query.PageSize ?? 50, 1, 500);
        IQueryable<RuleColumn> q = db.RuleColumns.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search)) { string s = query.Search.Trim(); q = q.Where(x => EF.Functions.ILike(x.Name, $"%{s}%") || EF.Functions.ILike(x.Key, $"%{s}%") || (x.HeaderNames != null && EF.Functions.ILike(x.HeaderNames, $"%{s}%"))); }
        if (query.Kind is { } kind) q = q.Where(x => x.Kind == kind);
        if (query.DataType is { } dataType) q = q.Where(x => x.DataType == dataType);
        if (query.IsActive is { } active) q = q.Where(x => x.IsActive == active);
        q = query.Sort?.ToUpperInvariant() switch { "-NAME" => q.OrderByDescending(x => x.Name), "KEY" => q.OrderBy(x => x.Key), "-UPDATED" => q.OrderByDescending(x => x.UpdatedAtUtc), _ => q.OrderBy(x => x.Name) };
        long total = await q.LongCountAsync(ct).ConfigureAwait(false);
        List<RuleColumn> rows = await q.Skip((page - 1) * size).Take(size).ToListAsync(ct).ConfigureAwait(false);
        return new() { Items = rows.Select(ColumnMap.Dto).ToList(), PageNumber = page, PageSize = size, TotalCount = total, TotalPages = (int)Math.Ceiling(total / (double)size) };
    }
}

public sealed class GetRuleColumnQueryHandler(RulesDbContext db) : IQueryHandler<GetRuleColumnQuery, RuleColumnDto>
{
    public async ValueTask<RuleColumnDto> Handle(GetRuleColumnQuery query, CancellationToken ct) => ColumnMap.Dto(await ColumnMap.Load(db, query.Id, false, ct).ConfigureAwait(false));
}

public sealed class CreateRuleColumnCommandHandler(RulesDbContext db, IMultiTenantContextAccessor<AppTenantInfo> tenant) : ICommandHandler<CreateRuleColumnCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateRuleColumnCommand c, CancellationToken ct)
    {
        string tenantId = tenant.MultiTenantContext.TenantInfo?.Id ?? throw new CustomException("Tenant is required.", (IEnumerable<string>?)null, HttpStatusCode.Unauthorized);
        RuleColumn column = RuleColumn.Create(tenantId, c.Name, c.Key, c.Kind, c.DataType, c.HeaderNames, c.Description, DateTime.UtcNow);
        await ColumnMap.EnsureUnique(db, column.Kind, column.Name, column.Key, null, ct).ConfigureAwait(false);
        db.RuleColumns.Add(column);
        await ColumnMap.Save(db, ct).ConfigureAwait(false);
        return column.Id;
    }
}

public sealed class UpdateRuleColumnCommandHandler(RulesDbContext db) : ICommandHandler<UpdateRuleColumnCommand>
{
    public async ValueTask<Unit> Handle(UpdateRuleColumnCommand c, CancellationToken ct)
    {
        RuleColumn column = await ColumnMap.Load(db, c.Id, true, ct).ConfigureAwait(false);
        await ColumnMap.EnsureUnique(db, column.Kind, c.Name.Trim(), c.Key.Trim(), column.Id, ct).ConfigureAwait(false);
        column.Update(c.ExpectedVersion, c.Name, c.Key, c.DataType, c.HeaderNames, c.Description, c.IsActive, DateTime.UtcNow);
        await ColumnMap.Save(db, ct).ConfigureAwait(false);
        return Unit.Value;
    }
}

public sealed class DeleteRuleColumnCommandHandler(RulesDbContext db) : ICommandHandler<DeleteRuleColumnCommand>
{
    public async ValueTask<Unit> Handle(DeleteRuleColumnCommand c, CancellationToken ct)
    {
        RuleColumn column = await ColumnMap.Load(db, c.Id, true, ct).ConfigureAwait(false);
        db.RuleColumns.Remove(column);
        await ColumnMap.Save(db, ct).ConfigureAwait(false);
        return Unit.Value;
    }
}

public sealed class SearchRuleColumnsQueryValidator : AbstractValidator<SearchRuleColumnsQuery>
{
    public SearchRuleColumnsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0); RuleFor(x => x.PageSize).InclusiveBetween(1, 500); RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Sort).Must(x => x is null or "name" or "-name" or "key" or "-updated");
        RuleFor(x => x.Kind).IsInEnum(); RuleFor(x => x.DataType).IsInEnum();
    }
}

public sealed class CreateRuleColumnCommandValidator : AbstractValidator<CreateRuleColumnCommand>
{
    public CreateRuleColumnCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(RuleColumn.NameMaxLength);
        RuleFor(x => x.Key).Must(RuleColumn.IsValidKey).WithMessage("Key must be dot-separated letters, numbers, dashes, or underscores (max 128).");
        RuleFor(x => x.Kind).IsInEnum(); RuleFor(x => x.DataType).IsInEnum();
        RuleFor(x => x.HeaderNames).MaximumLength(RuleColumn.HeaderNamesMaxLength); RuleFor(x => x.Description).MaximumLength(RuleColumn.DescriptionMaxLength);
    }
}

public sealed class UpdateRuleColumnCommandValidator : AbstractValidator<UpdateRuleColumnCommand>
{
    public UpdateRuleColumnCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty(); RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(RuleColumn.NameMaxLength);
        RuleFor(x => x.Key).Must(RuleColumn.IsValidKey).WithMessage("Key must be dot-separated letters, numbers, dashes, or underscores (max 128).");
        RuleFor(x => x.DataType).IsInEnum();
        RuleFor(x => x.HeaderNames).MaximumLength(RuleColumn.HeaderNamesMaxLength); RuleFor(x => x.Description).MaximumLength(RuleColumn.DescriptionMaxLength);
    }
}

public sealed class DeleteRuleColumnCommandValidator : AbstractValidator<DeleteRuleColumnCommand>
{
    public DeleteRuleColumnCommandValidator() { RuleFor(x => x.Id).NotEmpty(); }
}

internal static class ColumnMap
{
    internal static RuleColumnDto Dto(RuleColumn x) => new(x.Id, x.Name, x.Key, x.Kind, x.DataType, x.HeaderNames, x.Description, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc, x.Version);

    internal static async Task<RuleColumn> Load(RulesDbContext db, Guid id, bool tracking, CancellationToken ct)
    {
        IQueryable<RuleColumn> q = tracking ? db.RuleColumns : db.RuleColumns.AsNoTracking();
        return await q.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false) ?? throw new CustomException("Column was not found.", (IEnumerable<string>?)null, HttpStatusCode.NotFound);
    }

    internal static async Task EnsureUnique(RulesDbContext db, RuleColumnKind kind, string name, string key, Guid? exceptId, CancellationToken ct)
    {
        string namePattern = EscapeLike(name); string keyPattern = EscapeLike(key);
        bool clash = await db.RuleColumns.AnyAsync(x => x.Kind == kind && x.Id != exceptId && (EF.Functions.ILike(x.Name, namePattern) || EF.Functions.ILike(x.Key, keyPattern)), ct).ConfigureAwait(false);
        if (clash) throw new CustomException("A column with the same name or key already exists.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
    }

    private static string EscapeLike(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    internal static async Task Save(RulesDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException) { throw new CustomException("The column was modified by another request.", (IEnumerable<string>?)null, HttpStatusCode.Conflict); }
        catch (DbUpdateException) { throw new CustomException("The column conflicts with an existing column.", (IEnumerable<string>?)null, HttpStatusCode.Conflict); }
    }
}
