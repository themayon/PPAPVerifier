using System.Net;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Submissions;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Submissions.DecidePpapSubmission;

public sealed class DecidePpapSubmissionCommandHandler(PpapDbContext dbContext, ICurrentUser currentUser) : ICommandHandler<DecidePpapSubmissionCommand, PpapSubmissionDecisionDto>
{
    public async ValueTask<PpapSubmissionDecisionDto> Handle(DecidePpapSubmissionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid userId = currentUser.GetUserId();
        if (userId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        _ = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        if (!DecidePpapSubmissionCommandValidator.TryParse(command.Decision, out SubmissionDecision decision))
        {
            throw new CustomException("Decision must be Approved, InterimApproved, or Rejected.", (IEnumerable<string>?)null, HttpStatusCode.BadRequest);
        }

        PpapSubmission submission = await dbContext.PpapSubmissions.FirstOrDefaultAsync(x => x.Id == command.SubmissionId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException($"PPAP submission {command.SubmissionId} not found.");
        submission.Decide(decision, command.Comment, userId, command.ExpectedVersion, DateTime.UtcNow);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new CustomException("The submission was modified by another request.", (IEnumerable<string>?)null, HttpStatusCode.Conflict);
        }

        return new PpapSubmissionDecisionDto(submission.Id, submission.Status.ToString(), submission.Decision.ToString(), submission.DecisionComment,
            submission.DecidedByUserId, submission.DecidedAtUtc, submission.Version);
    }
}
