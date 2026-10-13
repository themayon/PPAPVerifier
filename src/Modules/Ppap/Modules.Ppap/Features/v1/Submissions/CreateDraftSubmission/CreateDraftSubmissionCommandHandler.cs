using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using DES.Modules.Rules.Contracts;
using Mediator;

namespace DES.Modules.Ppap.Features.v1.Submissions.CreateDraftSubmission;

public sealed class CreateDraftSubmissionCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser,
    IMediator mediator) : ICommandHandler<CreateDraftSubmissionCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateDraftSubmissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        string tenantId = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        RuleTemplateDetailDto? template = null;
        RuleTemplateVersionDto? templateVersion = null;
        if (command.RuleTemplateId.HasValue)
        {
            template = await mediator.Send(
                new GetTemplateQuery(command.RuleTemplateId.Value),
                cancellationToken).ConfigureAwait(false);
            if (template.Lifecycle != RuleLifecycle.Published || !template.CurrentPublishedVersionId.HasValue)
            {
                throw TemplateConflict("The selected rule template is not published.");
            }

            templateVersion = template.Versions.SingleOrDefault(
                x => x.Id == template.CurrentPublishedVersionId.Value);
            if (templateVersion is null || templateVersion.Status != RuleLifecycle.Published)
            {
                throw TemplateConflict("The selected rule template does not have a valid current published version.");
            }
        }

        PpapSubmission submission = PpapSubmission.CreateDraft(
            tenantId,
            ownerUserId,
            command.PartName,
            command.PartNumber,
            command.DrawingRevision,
            command.PpapLevel,
            template?.Id,
            templateVersion?.Id);
        if (templateVersion is not null)
        {
            foreach (TemplateDocumentRequirementDto requirement in templateVersion.DocumentRequirements.OrderBy(x => x.SortOrder))
            {
                submission.AddDocumentSlot(requirement.DocumentCode, MapDisposition(requirement.Disposition));
            }
        }

        dbContext.PpapSubmissions.Add(submission);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return submission.Id;
    }

    private static SubmissionDocumentDisposition MapDisposition(TemplateDocumentDisposition disposition) => disposition switch
    {
        TemplateDocumentDisposition.Submit => SubmissionDocumentDisposition.Submit,
        TemplateDocumentDisposition.Retain => SubmissionDocumentDisposition.Retain,
        TemplateDocumentDisposition.Available => SubmissionDocumentDisposition.Available,
        _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
    };

    private static CustomException TemplateConflict(string message) => new(
        message,
        (IEnumerable<string>?)null,
        HttpStatusCode.Conflict);
}
