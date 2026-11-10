using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Persistence;
using DES.Modules.Identity.Contracts.Services;
using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Contracts.Authorization;
using DES.Modules.Ppap.Contracts.v1.Parts;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Rules.Contracts;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Parts;

public sealed class SearchMyPpapPartsQueryHandler(PpapDbContext db, ICurrentUser currentUser, IUserPermissionService permissions) : IQueryHandler<SearchMyPpapPartsQuery, PagedResponse<PpapPartDto>>
{
    public async ValueTask<PagedResponse<PpapPartDto>> Handle(SearchMyPpapPartsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid userId = PartHandlerSupport.Owner(currentUser, db);
        int page = query.PageNumber ?? 1;
        int size = query.PageSize ?? 20;
        IQueryable<PpapPart> parts = await PartHandlerSupport.Scoped(db, permissions, userId, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{query.Search.Trim()}%";
            parts = parts.Where(x => EF.Functions.ILike(x.PartName, pattern) || EF.Functions.ILike(x.PartNumber, pattern) || EF.Functions.ILike(x.DrawingRevision, pattern)
                || db.PpapSuppliers.Any(s => s.Id == x.SupplierId && (EF.Functions.ILike(s.Name, pattern) || EF.Functions.ILike(s.Code, pattern))));
        }
        if (query.SupplierId is Guid supplierId) parts = parts.Where(x => x.SupplierId == supplierId);
        if (query.PpapLevel is int level) parts = parts.Where(x => x.PpapLevel == level);
        if (PartHandlerSupport.TryClassification(query.Classification, out PartClassification classification)) parts = parts.Where(x => x.Classification == classification);
        if (!string.IsNullOrWhiteSpace(query.Status)) parts = PartHandlerSupport.WithStatus(db, parts, query.Status);
        long total = await parts.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<PpapPartDto> items = await PartHandlerSupport.Directory(db, PartHandlerSupport.Sort(parts, query.Sort).Skip((page - 1) * size).Take(size), userId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResponse<PpapPartDto> { Items = items, PageNumber = page, PageSize = size, TotalCount = total, TotalPages = (int)Math.Ceiling(total / (double)size) };
    }
}

public sealed class GetPpapPartsSummaryQueryHandler(PpapDbContext db, ICurrentUser currentUser, IUserPermissionService permissions) : IQueryHandler<GetPpapPartsSummaryQuery, PpapPartsSummaryDto>
{
    public async ValueTask<PpapPartsSummaryDto> Handle(GetPpapPartsSummaryQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid userId = PartHandlerSupport.Owner(currentUser, db);
        IQueryable<PpapPart> parts = await PartHandlerSupport.Scoped(db, permissions, userId, cancellationToken).ConfigureAwait(false);
        IQueryable<PpapSubmission> submissions = db.PpapSubmissions.AsNoTracking().Where(s => parts.Any(p => p.Id == s.PartId));
        DateTime now = DateTime.UtcNow;
        DateTime monthStart = new(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime lastMonthStart = monthStart.AddMonths(-1);
        return new PpapPartsSummaryDto(
            await parts.LongCountAsync(cancellationToken).ConfigureAwait(false),
            await submissions.LongCountAsync(s => s.Status == SubmissionStatus.Draft || (s.Status == SubmissionStatus.Submitted && (s.Decision == SubmissionDecision.None || s.Decision == SubmissionDecision.InterimApproved)), cancellationToken).ConfigureAwait(false),
            await submissions.LongCountAsync(s => s.Status == SubmissionStatus.Submitted && s.Decision == SubmissionDecision.None, cancellationToken).ConfigureAwait(false),
            await submissions.LongCountAsync(s => s.Decision == SubmissionDecision.Approved && s.DecidedAtUtc >= monthStart, cancellationToken).ConfigureAwait(false),
            await submissions.LongCountAsync(s => s.Decision == SubmissionDecision.Approved && s.DecidedAtUtc >= lastMonthStart && s.DecidedAtUtc < monthStart, cancellationToken).ConfigureAwait(false),
            await submissions.LongCountAsync(s => s.CreatedAtUtc >= monthStart, cancellationToken).ConfigureAwait(false),
            await submissions.LongCountAsync(s => s.CreatedAtUtc >= lastMonthStart && s.CreatedAtUtc < monthStart, cancellationToken).ConfigureAwait(false));
    }
}

public sealed class GetPartQueryHandler(PpapDbContext db, ICurrentUser currentUser, IUserPermissionService permissions) : IQueryHandler<GetPartQuery, PpapPartDto>
{
    public async ValueTask<PpapPartDto> Handle(GetPartQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid userId = PartHandlerSupport.Owner(currentUser, db);
        IQueryable<PpapPart> parts = await PartHandlerSupport.Scoped(db, permissions, userId, cancellationToken).ConfigureAwait(false);
        return await PartHandlerSupport.Directory(db, parts.Where(x => x.Id == query.PartId), userId).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP part {query.PartId} not found.");
    }
}

public sealed class CreatePartCommandHandler(PpapDbContext db, ICurrentUser currentUser, IMediator mediator) : ICommandHandler<CreatePartCommand, PpapPartDto>
{
    public async ValueTask<PpapPartDto> Handle(CreatePartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid ownerId = PartHandlerSupport.Owner(currentUser, db);
        await PartHandlerSupport.EnsureActiveSupplier(db, command.SupplierId, null, cancellationToken).ConfigureAwait(false);
        PartHandlerSupport.TryClassification(command.Classification, out PartClassification classification);
        PpapPart part = PpapPart.Create(db.TenantInfo!.Id!, ownerId, command.PartName, command.PartNumber, command.DrawingRevision, command.SupplierId, command.PpapLevel, classification);
        if (command.RuleTemplateId is Guid templateId)
        {
            Guid versionId = await PartHandlerSupport.CurrentTemplateVersion(mediator, templateId, cancellationToken).ConfigureAwait(false);
            part.AssignTemplate(templateId, versionId, part.Version);
        }
        db.PpapParts.Add(part);
        await PartHandlerSupport.Save(db, cancellationToken).ConfigureAwait(false);
        return PartHandlerSupport.ToDto(part);
    }
}

public sealed class UpdatePartCommandHandler(PpapDbContext db, ICurrentUser currentUser) : ICommandHandler<UpdatePartCommand, PpapPartDto>
{
    public async ValueTask<PpapPartDto> Handle(UpdatePartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        PpapPart part = await PartHandlerSupport.LoadOwned(db, currentUser, command.PartId, cancellationToken).ConfigureAwait(false);
        await PartHandlerSupport.EnsureActiveSupplier(db, command.SupplierId, part.SupplierId, cancellationToken).ConfigureAwait(false);
        PartClassification classification = PartHandlerSupport.TryClassification(command.Classification, out PartClassification parsed) ? parsed : part.Classification;
        part.UpdateDetails(command.PartName, command.PartNumber, command.DrawingRevision, command.SupplierId, command.PpapLevel ?? part.PpapLevel, classification, command.ExpectedVersion);
        await PartHandlerSupport.Save(db, cancellationToken).ConfigureAwait(false);
        return PartHandlerSupport.ToDto(part);
    }
}

public sealed class AssignPartTemplateCommandHandler(PpapDbContext db, ICurrentUser currentUser, IMediator mediator) : ICommandHandler<AssignPartTemplateCommand, PpapPartDto>
{
    public async ValueTask<PpapPartDto> Handle(AssignPartTemplateCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        PpapPart part = await PartHandlerSupport.LoadOwned(db, currentUser, command.PartId, cancellationToken).ConfigureAwait(false);
        Guid versionId = await PartHandlerSupport.CurrentTemplateVersion(mediator, command.RuleTemplateId, cancellationToken).ConfigureAwait(false);
        part.AssignTemplate(command.RuleTemplateId, versionId, command.ExpectedVersion);
        await PartHandlerSupport.Save(db, cancellationToken).ConfigureAwait(false);
        return PartHandlerSupport.ToDto(part);
    }
}

public sealed class CreateDraftFromPartCommandHandler(PpapDbContext db, ICurrentUser currentUser, IMediator mediator) : ICommandHandler<CreateDraftFromPartCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateDraftFromPartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        PpapPart part = await PartHandlerSupport.LoadOwned(db, currentUser, command.PartId, cancellationToken).ConfigureAwait(false);
        if (part.RuleTemplateId is not Guid templateId)
        {
            throw PartHandlerSupport.Conflict("Assign a published rule template to the part before creating a draft.");
        }
        Guid versionId = await PartHandlerSupport.CurrentTemplateVersion(mediator, templateId, cancellationToken).ConfigureAwait(false);
        RuleTemplateVersionDto version = await mediator.Send(new GetTemplateVersionQuery(templateId, versionId), cancellationToken).ConfigureAwait(false);
        PpapSubmission submission = PpapSubmission.CreateDraft(
            part.TenantId, part.OwnerUserId, part.PartName, part.PartNumber, part.DrawingRevision,
            command.PpapLevel, templateId, versionId, part.Id);
        foreach (TemplateDocumentRequirementDto requirement in version.DocumentRequirements.OrderBy(x => x.SortOrder))
        {
            SubmissionDocumentDisposition disposition = requirement.Disposition switch
            {
                TemplateDocumentDisposition.Submit => SubmissionDocumentDisposition.Submit,
                TemplateDocumentDisposition.Retain => SubmissionDocumentDisposition.Retain,
                TemplateDocumentDisposition.Available => SubmissionDocumentDisposition.Available,
                _ => throw new InvalidOperationException("The template document disposition is invalid."),
            };
            submission.AddDocumentSlot(requirement.DocumentCode, disposition);
        }
        db.PpapSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return submission.Id;
    }
}

internal static class PartHandlerSupport
{
    internal static readonly IReadOnlyList<string> DirectoryStatuses = ["NotStarted", "Draft", "Submitted", "InterimApproved", "Approved", "Rejected"];

    internal static bool TryClassification(string? value, out PartClassification classification)
    {
        classification = PartClassification.Standard;
        if (string.IsNullOrWhiteSpace(value)) return false;
        foreach (PartClassification candidate in Enum.GetValues<PartClassification>())
        {
            if (string.Equals(candidate.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                classification = candidate;
                return true;
            }
        }
        return false;
    }

    internal static async Task<IQueryable<PpapPart>> Scoped(PpapDbContext db, IUserPermissionService permissions, Guid userId, CancellationToken cancellationToken)
    {
        bool reviewer = await permissions.HasPermissionAsync(userId.ToString(), PpapPermissions.Submissions.Approve, cancellationToken).ConfigureAwait(false);
        IQueryable<PpapPart> parts = db.PpapParts.AsNoTracking().Where(x => x.IsActive);
        return reviewer ? parts : parts.Where(x => x.OwnerUserId == userId);
    }

    internal static IQueryable<PpapPart> WithStatus(PpapDbContext db, IQueryable<PpapPart> parts, string status)
    {
        IQueryable<PpapSubmission> live = db.PpapSubmissions.Where(s => s.Status != SubmissionStatus.Cancelled);
        return status switch
        {
            "NotStarted" => parts.Where(x => !live.Any(s => s.PartId == x.Id)),
            "Draft" => parts.Where(x => live.Where(s => s.PartId == x.Id).OrderByDescending(s => s.CreatedAtUtc).Take(1).Any(s => s.Status == SubmissionStatus.Draft)),
            "Submitted" => parts.Where(x => live.Where(s => s.PartId == x.Id).OrderByDescending(s => s.CreatedAtUtc).Take(1).Any(s => s.Status == SubmissionStatus.Submitted && s.Decision == SubmissionDecision.None)),
            "InterimApproved" => parts.Where(x => live.Where(s => s.PartId == x.Id).OrderByDescending(s => s.CreatedAtUtc).Take(1).Any(s => s.Decision == SubmissionDecision.InterimApproved)),
            "Approved" => parts.Where(x => live.Where(s => s.PartId == x.Id).OrderByDescending(s => s.CreatedAtUtc).Take(1).Any(s => s.Decision == SubmissionDecision.Approved)),
            "Rejected" => parts.Where(x => live.Where(s => s.PartId == x.Id).OrderByDescending(s => s.CreatedAtUtc).Take(1).Any(s => s.Decision == SubmissionDecision.Rejected)),
            _ => parts,
        };
    }

    internal static IQueryable<PpapPartDto> Directory(PpapDbContext db, IQueryable<PpapPart> parts, Guid userId) => parts.Select(x => new PpapPartDto(
        x.Id, x.PartName, x.PartNumber, x.DrawingRevision, x.RuleTemplateId, x.RuleTemplateVersionId, x.IsActive, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc, x.SupplierId,
        x.PpapLevel, x.Classification.ToString(), x.OwnerUserId == userId,
        db.PpapSuppliers.Where(s => s.Id == x.SupplierId).Select(s => new PpapPartSupplierSummaryDto(s.Id, s.Code, s.Name, s.City, s.Country, s.IsActive)).FirstOrDefault(),
        db.PpapSubmissions.Where(s => s.PartId == x.Id && s.Status != SubmissionStatus.Cancelled).OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new PpapPartSubmissionSummaryDto(
                s.Id, s.Status.ToString(), s.Decision.ToString(), s.PpapLevel,
                s.Documents.Count(d => d.Status != SubmissionDocumentStatus.NotApplicable),
                s.Documents.Count(d => d.Status == SubmissionDocumentStatus.Available),
                db.PpapReports.Where(r => r.SubmissionId == s.Id).OrderByDescending(r => r.GeneratedAtUtc).Select(r => (Guid?)r.Id).FirstOrDefault(),
                db.PpapReports.Where(r => r.SubmissionId == s.Id).OrderByDescending(r => r.GeneratedAtUtc).Select(r => r.FailedFindingCount + r.ErrorFindingCount).FirstOrDefault(),
                s.OwnerUserId == userId, s.Version, s.CreatedAtUtc, s.SubmittedAtUtc, s.DecidedAtUtc))
            .FirstOrDefault()));

    internal static async Task<Guid> CurrentTemplateVersion(IMediator mediator, Guid templateId, CancellationToken cancellationToken)
    {
        RuleTemplateDetailDto template = await mediator.Send(new GetTemplateQuery(templateId), cancellationToken).ConfigureAwait(false);
        if (template.Lifecycle != RuleLifecycle.Published || template.CurrentPublishedVersionId is not Guid versionId ||
            template.Versions.SingleOrDefault(x => x.Id == versionId)?.Status != RuleLifecycle.Published)
        {
            throw Conflict("The selected rule template does not have a current published version.");
        }
        return versionId;
    }

    internal static async Task EnsureActiveSupplier(PpapDbContext db, Guid? supplierId, Guid? currentSupplierId, CancellationToken cancellationToken)
    {
        if (supplierId is not Guid id || id == Guid.Empty) return;
        bool active = await db.PpapSuppliers.AnyAsync(x => x.Id == id && (x.IsActive || x.Id == currentSupplierId), cancellationToken).ConfigureAwait(false);
        if (!active) throw new CustomException("The selected supplier was not found or is inactive.", (IEnumerable<string>?)null, HttpStatusCode.BadRequest);
    }

    internal static Guid Owner(ICurrentUser user, PpapDbContext db)
    {
        Guid ownerId = user.GetUserId();
        if (ownerId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        _ = db.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        return ownerId;
    }

    internal static async Task<PpapPart> LoadOwned(PpapDbContext db, ICurrentUser user, Guid id, CancellationToken cancellationToken)
    {
        Guid ownerId = Owner(user, db);
        return await db.PpapParts.SingleOrDefaultAsync(x => x.Id == id && x.OwnerUserId == ownerId && x.IsActive, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP part {id} not found.");
    }

    internal static IOrderedQueryable<PpapPart> Sort(IQueryable<PpapPart> parts, string? sort) => sort?.ToUpperInvariant() switch
    {
        "PARTNAME" => parts.OrderBy(x => x.PartName).ThenBy(x => x.Id),
        "-PARTNAME" => parts.OrderByDescending(x => x.PartName).ThenByDescending(x => x.Id),
        "PARTNUMBER" => parts.OrderBy(x => x.PartNumber).ThenBy(x => x.Id),
        "-PARTNUMBER" => parts.OrderByDescending(x => x.PartNumber).ThenByDescending(x => x.Id),
        "UPDATEDAT" => parts.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
        "-UPDATEDAT" => parts.OrderByDescending(x => x.UpdatedAtUtc).ThenByDescending(x => x.Id),
        _ => parts.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
    };

    internal static PpapPartDto ToDto(PpapPart part) => new(part.Id, part.PartName, part.PartNumber, part.DrawingRevision, part.RuleTemplateId, part.RuleTemplateVersionId, part.IsActive, part.Version, part.CreatedAtUtc, part.UpdatedAtUtc, part.SupplierId, part.PpapLevel, part.Classification.ToString());

    internal static async Task Save(PpapDbContext db, CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException) { throw Conflict("The PPAP part was modified by another request."); }
        catch (DbUpdateException) { throw Conflict("An active part with this part number already exists."); }
    }

    internal static CustomException Conflict(string message) => new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
