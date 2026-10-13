using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Submissions.UpdateDraftSubmission;

public sealed class UpdateDraftSubmissionCommandHandler(
    PpapDbContext dbContext,
    ICurrentUser currentUser) : ICommandHandler<UpdateDraftSubmissionCommand, PpapSubmissionDto>
{
    public async ValueTask<PpapSubmissionDto> Handle(
        UpdateDraftSubmissionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid ownerUserId = currentUser.GetUserId();
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedException("An authenticated user is required.");
        }
        _ = dbContext.TenantInfo?.Id
            ?? throw new UnauthorizedException("Tenant context is required.");

        PpapSubmission submission = await dbContext.PpapSubmissions
            .FirstOrDefaultAsync(
                x => x.Id == command.Id && x.OwnerUserId == ownerUserId,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP submission {command.Id} not found.");
        if (submission.Version != command.ExpectedVersion)
        {
            throw ConcurrencyConflict(command.ExpectedVersion, submission.Version);
        }

        submission.UpdateDraft(
            command.PartName,
            command.PartNumber,
            command.DrawingRevision,
            command.PpapLevel);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw ConcurrencyConflict(command.ExpectedVersion);
        }

        return ToDto(submission);
    }

    private static PpapSubmissionDto ToDto(PpapSubmission submission) => new(
        submission.Id,
        submission.PartId,
        submission.PartName,
        submission.PartNumber,
        submission.DrawingRevision,
        submission.PpapLevel,
        submission.RuleTemplateId,
        submission.RuleTemplateVersionId,
        submission.Status.ToString(),
        submission.Version,
        submission.CreatedAtUtc,
        submission.UpdatedAtUtc,
        submission.SubmittedAtUtc,
        submission.CancelledAtUtc);

    private static CustomException ConcurrencyConflict(int expectedVersion, int? currentVersion = null) =>
        new(
            currentVersion.HasValue
                ? $"PPAP submission version conflict. Expected version {expectedVersion}, but current version is {currentVersion.Value}."
                : $"PPAP submission version conflict. Expected version {expectedVersion}; the submission was modified by another request.",
            (IEnumerable<string>?)null,
            HttpStatusCode.Conflict);
}
