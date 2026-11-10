using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Shared.Persistence;
using DES.Framework.Web.Idempotency;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Parts;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using FluentValidation;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Suppliers;

public static class SearchPpapSuppliersEndpoint
{
    internal static RouteHandlerBuilder MapSearchPpapSuppliersEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/suppliers", ([AsParameters] SearchPpapSuppliersQuery query, IMediator mediator, CancellationToken ct) => mediator.Send(query, ct))
            .WithName("SearchPpapSuppliers").WithSummary("Search suppliers").RequirePermission(PpapPermissions.Suppliers.View).Produces<PagedResponse<PpapSupplierDto>>();
}

public static class GetPpapSuppliersSummaryEndpoint
{
    internal static RouteHandlerBuilder MapGetPpapSuppliersSummaryEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/suppliers/summary", (IMediator mediator, CancellationToken ct) => mediator.Send(new GetPpapSuppliersSummaryQuery(), ct))
            .WithName("GetPpapSuppliersSummary").WithSummary("Supplier directory statistics").RequirePermission(PpapPermissions.Suppliers.View).Produces<PpapSuppliersSummaryDto>();
}

public static class GetPpapSupplierEndpoint
{
    internal static RouteHandlerBuilder MapGetPpapSupplierEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/suppliers/{id:guid}", (Guid id, IMediator mediator, CancellationToken ct) => mediator.Send(new GetPpapSupplierQuery(id), ct))
            .WithName("GetPpapSupplier").WithSummary("Get a supplier").RequirePermission(PpapPermissions.Suppliers.View).Produces<PpapSupplierDto>();
}

public static class CreatePpapSupplierEndpoint
{
    internal static RouteHandlerBuilder MapCreatePpapSupplierEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/suppliers", async ([FromBody] CreatePpapSupplierCommand command, IMediator mediator, CancellationToken ct) =>
        {
            PpapSupplierDto supplier = await mediator.Send(command, ct);
            return Results.Created($"/api/v1/ppap/suppliers/{supplier.Id}", supplier);
        }).WithName("CreatePpapSupplier").WithSummary("Create a supplier").RequirePermission(PpapPermissions.Suppliers.Create).WithIdempotency().Produces<PpapSupplierDto>(StatusCodes.Status201Created);
}

public static class UpdatePpapSupplierEndpoint
{
    public sealed record UpdateSupplierRequest(int ExpectedVersion, string Code, PpapSupplierProfileInput Profile, bool IsActive, IReadOnlyList<Guid>? UserIds, IReadOnlyList<PpapSupplierCertificateItem>? Certificates);

    internal static RouteHandlerBuilder MapUpdatePpapSupplierEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPut("/suppliers/{id:guid}", (Guid id, [FromBody] UpdateSupplierRequest body, IMediator mediator, CancellationToken ct) =>
                mediator.Send(new UpdatePpapSupplierCommand(id, body.ExpectedVersion, body.Code, body.Profile, body.IsActive, body.UserIds, body.Certificates), ct))
            .WithName("UpdatePpapSupplier").WithSummary("Update a supplier").RequirePermission(PpapPermissions.Suppliers.Update).Produces<PpapSupplierDto>();
}

public sealed class SearchPpapSuppliersQueryHandler(PpapDbContext db) : IQueryHandler<SearchPpapSuppliersQuery, PagedResponse<PpapSupplierDto>>
{
    public async ValueTask<PagedResponse<PpapSupplierDto>> Handle(SearchPpapSuppliersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        int page = query.PageNumber ?? 1; int size = query.PageSize ?? 20;
        IQueryable<PpapSupplier> suppliers = db.PpapSuppliers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{query.Search.Trim()}%";
            suppliers = suppliers.Where(x => EF.Functions.ILike(x.Code, pattern) || EF.Functions.ILike(x.Name, pattern) || (x.ContactName != null && EF.Functions.ILike(x.ContactName, pattern)) || (x.Email != null && EF.Functions.ILike(x.Email, pattern)));
        }
        if (query.IsActive is bool active) suppliers = suppliers.Where(x => x.IsActive == active);
        if (!string.IsNullOrWhiteSpace(query.Region)) suppliers = suppliers.Where(x => x.Region == query.Region);
        if (!string.IsNullOrWhiteSpace(query.TierLevel)) suppliers = query.TierLevel == SupplierSupport.Unassigned ? suppliers.Where(x => x.TierLevel == null) : suppliers.Where(x => x.TierLevel == query.TierLevel);
        if (!string.IsNullOrWhiteSpace(query.QualityGrade)) suppliers = query.QualityGrade == SupplierSupport.Unassigned ? suppliers.Where(x => x.QualityGrade == null) : suppliers.Where(x => x.QualityGrade == query.QualityGrade);
        suppliers = query.Sort?.ToUpperInvariant() switch { "-NAME" => suppliers.OrderByDescending(x => x.Name), "CODE" => suppliers.OrderBy(x => x.Code), "-UPDATED" => suppliers.OrderByDescending(x => x.UpdatedAtUtc), _ => suppliers.OrderBy(x => x.Name) };
        long total = await suppliers.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<PpapSupplierDto> items = await SupplierSupport.Load(db, suppliers.Skip((page - 1) * size).Take(size), cancellationToken).ConfigureAwait(false);
        return new PagedResponse<PpapSupplierDto> { Items = items, PageNumber = page, PageSize = size, TotalCount = total, TotalPages = (int)Math.Ceiling(total / (double)size) };
    }
}

public sealed class GetPpapSupplierQueryHandler(PpapDbContext db) : IQueryHandler<GetPpapSupplierQuery, PpapSupplierDto>
{
    public async ValueTask<PpapSupplierDto> Handle(GetPpapSupplierQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        return (await SupplierSupport.Load(db, db.PpapSuppliers.AsNoTracking().Where(x => x.Id == query.SupplierId), cancellationToken).ConfigureAwait(false)).SingleOrDefault()
            ?? throw new NotFoundException($"Supplier {query.SupplierId} not found.");
    }
}

public sealed class GetPpapSuppliersSummaryQueryHandler(PpapDbContext db) : IQueryHandler<GetPpapSuppliersSummaryQuery, PpapSuppliersSummaryDto>
{
    public async ValueTask<PpapSuppliersSummaryDto> Handle(GetPpapSuppliersSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        DateTime now = DateTime.UtcNow;
        DateTime monthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime lastMonthStart = monthStart.AddMonths(-1);
        IQueryable<PpapSupplier> suppliers = db.PpapSuppliers.AsNoTracking();
        long total = await suppliers.LongCountAsync(cancellationToken).ConfigureAwait(false);
        long active = await suppliers.LongCountAsync(x => x.IsActive, cancellationToken).ConfigureAwait(false);
        long newThisMonth = await suppliers.LongCountAsync(x => x.CreatedAtUtc >= monthStart, cancellationToken).ConfigureAwait(false);
        long newLastMonth = await suppliers.LongCountAsync(x => x.CreatedAtUtc >= lastMonthStart && x.CreatedAtUtc < monthStart, cancellationToken).ConfigureAwait(false);
        int regions = await suppliers.Where(x => x.IsActive && x.Region != null).Select(x => x.Region).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);

        IQueryable<PpapSubmission> supplierSubmissions = db.PpapSubmissions.AsNoTracking()
            .Where(s => db.PpapParts.Any(p => p.Id == s.PartId && p.SupplierId != null));
        long activePpaps = await supplierSubmissions.LongCountAsync(
            s => s.Status == SubmissionStatus.Draft || (s.Status == SubmissionStatus.Submitted && (s.Decision == SubmissionDecision.None || s.Decision == SubmissionDecision.InterimApproved)),
            cancellationToken).ConfigureAwait(false);
        IQueryable<PpapReport> latest = db.PpapReports.AsNoTracking()
            .Where(r => supplierSubmissions.Any(s => s.Id == r.SubmissionId && s.Status != SubmissionStatus.Cancelled)
                && r.GeneratedAtUtc == db.PpapReports.Where(o => o.SubmissionId == r.SubmissionId).Max(o => o.GeneratedAtUtc));
        long critical = await latest.LongCountAsync(
            r => r.FailedFindingCount + r.ErrorFindingCount > 0
                && supplierSubmissions.Any(s => s.Id == r.SubmissionId && s.Status == SubmissionStatus.Submitted && (s.Decision == SubmissionDecision.None || s.Decision == SubmissionDecision.InterimApproved)),
            cancellationToken).ConfigureAwait(false);
        int evaluated = await latest.SumAsync(r => r.FindingCount - r.NotEvaluatedFindingCount, cancellationToken).ConfigureAwait(false);
        int passed = await latest.SumAsync(r => r.FindingCount - r.NotEvaluatedFindingCount - r.FailedFindingCount - r.ErrorFindingCount, cancellationToken).ConfigureAwait(false);
        return new PpapSuppliersSummaryDto(total, active, newThisMonth, newLastMonth, SupplierSupport.Score(evaluated, passed), activePpaps, regions, critical);
    }
}

public sealed class CreatePpapSupplierCommandHandler(PpapDbContext db) : ICommandHandler<CreatePpapSupplierCommand, PpapSupplierDto>
{
    public async ValueTask<PpapSupplierDto> Handle(CreatePpapSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        string tenantId = db.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        bool generated = string.IsNullOrWhiteSpace(command.Code);
        const int maxAttempts = 3;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            string code = generated ? await SupplierSupport.NextVendorCode(db, cancellationToken).ConfigureAwait(false) : command.Code!;
            PpapSupplier supplier = PpapSupplier.Create(tenantId, code, SupplierSupport.Profile(command.Profile), command.UserIds ?? [], SupplierSupport.Certificates(command.Certificates), DateTime.UtcNow);
            db.PpapSuppliers.Add(supplier);
            try
            {
                await SupplierSupport.Save(db, cancellationToken).ConfigureAwait(false);
                return (await SupplierSupport.Load(db, db.PpapSuppliers.AsNoTracking().Where(x => x.Id == supplier.Id), cancellationToken).ConfigureAwait(false)).Single();
            }
            catch (CustomException) when (generated && attempt < maxAttempts)
            {
                db.ChangeTracker.Clear();
            }
        }
        throw new CustomException("A vendor code could not be generated. Please try again.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
    }
}

public sealed class UpdatePpapSupplierCommandHandler(PpapDbContext db) : ICommandHandler<UpdatePpapSupplierCommand, PpapSupplierDto>
{
    public async ValueTask<PpapSupplierDto> Handle(UpdatePpapSupplierCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        PpapSupplier supplier = await db.PpapSuppliers.Include(x => x.Users).Include(x => x.Certificates).AsSplitQuery().SingleOrDefaultAsync(x => x.Id == command.SupplierId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"Supplier {command.SupplierId} not found.");
        supplier.Update(command.ExpectedVersion, command.Code, SupplierSupport.Profile(command.Profile), command.IsActive, command.UserIds ?? [], SupplierSupport.Certificates(command.Certificates), DateTime.UtcNow);
        await SupplierSupport.Save(db, cancellationToken).ConfigureAwait(false);
        return (await SupplierSupport.Load(db, db.PpapSuppliers.AsNoTracking().Where(x => x.Id == supplier.Id), cancellationToken).ConfigureAwait(false)).Single();
    }
}

public sealed class SearchPpapSuppliersQueryValidator : AbstractValidator<SearchPpapSuppliersQuery>
{
    public SearchPpapSuppliersQueryValidator()
    {
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Sort).Must(x => x is null or "name" or "-name" or "code" or "-updated");
        RuleFor(x => x.Region).Must(x => x is null || PpapRegions.All.Contains(x)).WithMessage("Unknown region.");
        RuleFor(x => x.TierLevel).Must(x => x is null or SupplierSupport.Unassigned || PpapSupplier.TierLevels.Contains(x)).WithMessage("Unknown tier level.");
        RuleFor(x => x.QualityGrade).Must(x => x is null or SupplierSupport.Unassigned || PpapSupplier.QualityGrades.Contains(x)).WithMessage("Unknown quality grade.");
    }
}

public sealed class CreatePpapSupplierCommandValidator : AbstractValidator<CreatePpapSupplierCommand>
{
    public CreatePpapSupplierCommandValidator()
    {
        RuleFor(x => x.Code).MaximumLength(50).Matches("^[A-Za-z0-9_.-]+$").When(x => !string.IsNullOrWhiteSpace(x.Code));
        RuleFor(x => x.Profile).NotNull().SetValidator(new PpapSupplierProfileInputValidator());
        RuleFor(x => x.UserIds).Must(x => x is null || x.Count <= PpapSupplier.MaxLinkedUsers).WithMessage($"A supplier can have at most {PpapSupplier.MaxLinkedUsers} linked users.");
        RuleFor(x => x.Certificates).Must(x => x is null || x.Count <= PpapSupplier.MaxCertificates).WithMessage($"A supplier can have at most {PpapSupplier.MaxCertificates} certificates.");
        RuleForEach(x => x.Certificates).SetValidator(new PpapSupplierCertificateItemValidator());
    }
}

public sealed class UpdatePpapSupplierCommandValidator : AbstractValidator<UpdatePpapSupplierCommand>
{
    public UpdatePpapSupplierCommandValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.ExpectedVersion).GreaterThan(0);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9_.-]+$");
        RuleFor(x => x.Profile).NotNull().SetValidator(new PpapSupplierProfileInputValidator());
        RuleFor(x => x.UserIds).Must(x => x is null || x.Count <= PpapSupplier.MaxLinkedUsers).WithMessage($"A supplier can have at most {PpapSupplier.MaxLinkedUsers} linked users.");
        RuleFor(x => x.Certificates).Must(x => x is null || x.Count <= PpapSupplier.MaxCertificates).WithMessage($"A supplier can have at most {PpapSupplier.MaxCertificates} certificates.");
        RuleForEach(x => x.Certificates).SetValidator(new PpapSupplierCertificateItemValidator());
    }
}

public sealed class PpapSupplierProfileInputValidator : AbstractValidator<PpapSupplierProfileInput>
{
    public PpapSupplierProfileInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TaxId).MaximumLength(50);
        RuleFor(x => x.DunsNumber).Matches(@"^\d{2}-?\d{3}-?\d{4}$").WithMessage("DUNS number must be 9 digits (00-000-0000).").When(x => !string.IsNullOrWhiteSpace(x.DunsNumber));
        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Country).MaximumLength(100);
        RuleFor(x => x.SiteName).MaximumLength(200);
        RuleFor(x => x.SiteAddress).MaximumLength(500);
        RuleFor(x => x.ContactName).MaximumLength(200);
        RuleFor(x => x.ContactJobTitle).MaximumLength(200);
        RuleFor(x => x.Email).MaximumLength(256).EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.Phone).MaximumLength(50);
        RuleFor(x => x.TierLevel).Must(x => string.IsNullOrWhiteSpace(x) || PpapSupplier.TierLevels.Contains(x, StringComparer.OrdinalIgnoreCase)).WithMessage("Tier level must be Tier 1, Tier 2, or Tier 3.");
        RuleFor(x => x.QualityGrade).Must(x => string.IsNullOrWhiteSpace(x) || PpapSupplier.QualityGrades.Contains(x, StringComparer.OrdinalIgnoreCase)).WithMessage("Quality grade must be A+, A, B, C, or D.");
    }
}

public sealed class PpapSupplierCertificateItemValidator : AbstractValidator<PpapSupplierCertificateItem>
{
    public PpapSupplierCertificateItemValidator()
    {
        RuleFor(x => x.FileAssetId).NotEmpty();
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(260);
        RuleFor(x => x.CertificateType).Must(x => PpapSupplier.CertificateTypes.Contains(x, StringComparer.OrdinalIgnoreCase)).WithMessage("Certificate type must be ISO 9001, IATF 16949, ISO 14001, or Other.");
    }
}

internal static class SupplierSupport
{
    private const string VendorPrefix = "VND-";

    internal static PpapSupplierProfile Profile(PpapSupplierProfileInput x) => new(
        x.Name, x.TaxId, x.DunsNumber, x.Address, x.City, x.Country, x.SiteName, x.SiteAddress, x.SiteSameAsHeadquarters, x.ContactName, x.ContactJobTitle, x.Email, x.Phone, x.TierLevel, x.QualityGrade);

    internal static IEnumerable<PpapSupplierCertificateInput> Certificates(IReadOnlyList<PpapSupplierCertificateItem>? items) =>
        (items ?? []).Select(x => new PpapSupplierCertificateInput(x.FileAssetId, x.FileName, x.CertificateType));

    internal static async Task<string> NextVendorCode(PpapDbContext db, CancellationToken cancellationToken)
    {
        List<string> codes = await db.PpapSuppliers.AsNoTracking().Where(x => x.Code.StartsWith(VendorPrefix)).Select(x => x.Code).ToListAsync(cancellationToken).ConfigureAwait(false);
        int next = codes.Select(code => int.TryParse(code.AsSpan(VendorPrefix.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{VendorPrefix}{next:D6}");
    }

    internal const string Unassigned = "unassigned";

    internal static double? Score(int evaluated, int passed) => evaluated > 0 ? Math.Round(passed * 100.0 / evaluated, 1) : null;

    internal static async Task<List<PpapSupplierDto>> Load(PpapDbContext db, IQueryable<PpapSupplier> suppliers, CancellationToken cancellationToken)
    {
        var rows = await suppliers.Select(x => new
        {
            Dto = new PpapSupplierDto(
                x.Id, x.Code, x.Name, x.ContactName, x.Email, x.Phone, x.Address, x.IsActive,
                x.Users.Select(u => u.UserId).ToList(),
                db.PpapParts.Count(p => p.SupplierId == x.Id && p.IsActive),
                x.CreatedAtUtc, x.UpdatedAtUtc, x.Version,
                x.TaxId, x.DunsNumber, x.City, x.Country, x.SiteName, x.SiteAddress, x.SiteSameAsHeadquarters, x.ContactJobTitle,
                x.Certificates.OrderBy(c => c.AddedAtUtc).Select(c => new PpapSupplierCertificateDto(c.FileAssetId, c.FileName, c.CertificateType, c.AddedAtUtc)).ToList(),
                x.Region, x.TierLevel, x.QualityGrade, null,
                db.PpapSubmissions.Count(s => db.PpapParts.Any(p => p.Id == s.PartId && p.SupplierId == x.Id)
                    && (s.Status == SubmissionStatus.Draft || (s.Status == SubmissionStatus.Submitted && (s.Decision == SubmissionDecision.None || s.Decision == SubmissionDecision.InterimApproved))))),
            Evaluated = db.PpapReports
                .Where(r => db.PpapSubmissions.Any(s => s.Id == r.SubmissionId && s.Status != SubmissionStatus.Cancelled && db.PpapParts.Any(p => p.Id == s.PartId && p.SupplierId == x.Id))
                    && r.GeneratedAtUtc == db.PpapReports.Where(o => o.SubmissionId == r.SubmissionId).Max(o => o.GeneratedAtUtc))
                .Sum(r => (int?)(r.FindingCount - r.NotEvaluatedFindingCount)) ?? 0,
            Passed = db.PpapReports
                .Where(r => db.PpapSubmissions.Any(s => s.Id == r.SubmissionId && s.Status != SubmissionStatus.Cancelled && db.PpapParts.Any(p => p.Id == s.PartId && p.SupplierId == x.Id))
                    && r.GeneratedAtUtc == db.PpapReports.Where(o => o.SubmissionId == r.SubmissionId).Max(o => o.GeneratedAtUtc))
                .Sum(r => (int?)(r.FindingCount - r.NotEvaluatedFindingCount - r.FailedFindingCount - r.ErrorFindingCount)) ?? 0,
        }).AsSplitQuery().ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(r => r.Dto with { QualityScore = Score(r.Evaluated, r.Passed) }).ToList();
    }

    internal static async Task Save(PpapDbContext db, CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException) { throw new CustomException("The supplier was modified by another request.", (IEnumerable<string>?)null, HttpStatusCode.Conflict); }
        catch (DbUpdateException) { throw new CustomException("A supplier with this code already exists.", (IEnumerable<string>?)null, HttpStatusCode.Conflict); }
    }
}
