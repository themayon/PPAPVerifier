using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Contracts.v1.Parts;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Rules.Contracts;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Parts;

public sealed class SearchMyPpapPartsQueryHandler(PpapDbContext db, ICurrentUser currentUser) : IQueryHandler<SearchMyPpapPartsQuery, PagedResponse<PpapPartDto>>
{
    public async ValueTask<PagedResponse<PpapPartDto>> Handle(SearchMyPpapPartsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid ownerId = PartHandlerSupport.Owner(currentUser, db);
        int page = query.PageNumber ?? 1;
        int size = query.PageSize ?? 20;
        IQueryable<PpapPart> parts = db.PpapParts.AsNoTracking().Where(x => x.OwnerUserId == ownerId && x.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{query.Search.Trim()}%";
            parts = parts.Where(x => EF.Functions.ILike(x.PartName, pattern) || EF.Functions.ILike(x.PartNumber, pattern) || EF.Functions.ILike(x.DrawingRevision, pattern));
        }
        long total = await parts.LongCountAsync(cancellationToken).ConfigureAwait(false);
        List<PpapPartDto> items = await PartHandlerSupport.Sort(parts, query.Sort)
            .Skip((page - 1) * size).Take(size).Select(PartHandlerSupport.Projection).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PagedResponse<PpapPartDto> { Items = items, PageNumber = page, PageSize = size, TotalCount = total, TotalPages = (int)Math.Ceiling(total / (double)size) };
    }
}

public sealed class GetPartQueryHandler(PpapDbContext db, ICurrentUser currentUser) : IQueryHandler<GetPartQuery, PpapPartDto>
{
    public async ValueTask<PpapPartDto> Handle(GetPartQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Guid ownerId = PartHandlerSupport.Owner(currentUser, db);
        return await db.PpapParts.AsNoTracking().Where(x => x.Id == query.PartId && x.OwnerUserId == ownerId && x.IsActive)
            .Select(PartHandlerSupport.Projection).SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP part {query.PartId} not found.");
    }
}

public sealed class CreatePartCommandHandler(PpapDbContext db, ICurrentUser currentUser) : ICommandHandler<CreatePartCommand, PpapPartDto>
{
    public async ValueTask<PpapPartDto> Handle(CreatePartCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid ownerId = PartHandlerSupport.Owner(currentUser, db);
        PpapPart part = PpapPart.Create(db.TenantInfo!.Id!, ownerId, command.PartName, command.PartNumber, command.DrawingRevision);
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
        part.UpdateDetails(command.PartName, command.PartNumber, command.DrawingRevision, command.ExpectedVersion);
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
        RuleTemplateDetailDto template = await mediator.Send(new GetTemplateQuery(command.RuleTemplateId), cancellationToken).ConfigureAwait(false);
        if (template.Lifecycle != RuleLifecycle.Published || template.CurrentPublishedVersionId is not Guid versionId ||
            template.Versions.SingleOrDefault(x => x.Id == versionId)?.Status != RuleLifecycle.Published)
        {
            throw PartHandlerSupport.Conflict("The selected rule template does not have a current published version.");
        }
        part.AssignTemplate(template.Id, versionId, command.ExpectedVersion);
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
        if (part.RuleTemplateId is not Guid templateId || part.RuleTemplateVersionId is not Guid versionId)
        {
            throw PartHandlerSupport.Conflict("Assign a published rule template to the part before creating a draft.");
        }
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
    internal static readonly System.Linq.Expressions.Expression<Func<PpapPart, PpapPartDto>> Projection = x => new PpapPartDto(
        x.Id, x.PartName, x.PartNumber, x.DrawingRevision, x.RuleTemplateId, x.RuleTemplateVersionId, x.IsActive, x.Version, x.CreatedAtUtc, x.UpdatedAtUtc);

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

    internal static PpapPartDto ToDto(PpapPart part) => new(part.Id, part.PartName, part.PartNumber, part.DrawingRevision, part.RuleTemplateId, part.RuleTemplateVersionId, part.IsActive, part.Version, part.CreatedAtUtc, part.UpdatedAtUtc);

    internal static async Task Save(PpapDbContext db, CancellationToken cancellationToken)
    {
        try { await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false); }
        catch (DbUpdateConcurrencyException) { throw Conflict("The PPAP part was modified by another request."); }
        catch (DbUpdateException) { throw Conflict("An active part with this part number already exists."); }
    }

    internal static CustomException Conflict(string message) => new(message, (IEnumerable<string>?)null, HttpStatusCode.Conflict);
}
