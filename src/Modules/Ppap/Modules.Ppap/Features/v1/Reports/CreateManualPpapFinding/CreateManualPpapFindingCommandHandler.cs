using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Ppap.Contracts.v1.Reports;
using DES.Modules.Ppap.Data;
using DES.Modules.Ppap.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Features.v1.Reports.CreateManualPpapFinding;

public sealed class CreateManualPpapFindingCommandHandler(PpapDbContext dbContext, ICurrentUser currentUser)
    : ICommandHandler<CreateManualPpapFindingCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateManualPpapFindingCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        Guid actorId = currentUser.GetUserId();
        if (actorId == Guid.Empty) throw new UnauthorizedException("An authenticated user is required.");
        string tenantId = dbContext.TenantInfo?.Id ?? throw new UnauthorizedException("Tenant context is required.");
        PpapReport report = await dbContext.PpapReports
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == command.ReportId)
            .Join(
                dbContext.PpapJobs.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == PpapJobStatus.Completed),
                x => x.JobId,
                x => x.Id,
                (report, job) => report)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Completed PPAP report {command.ReportId} not found.");

        PpapSubmissionDocument? document = null;
        if (command.SubmissionDocumentId is not null)
        {
            document = await dbContext.PpapSubmissionDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId
                    && x.Id == command.SubmissionDocumentId
                    && x.SubmissionId == report.SubmissionId
                    && x.CanonicalDocumentId == command.CanonicalDocumentId
                    && x.ExtractionAttemptId == command.ExtractionAttemptId,
                    cancellationToken)
                .ConfigureAwait(false);
            if (document is null) throw new NotFoundException("The document correlation does not belong to the report submission.");
        }

        PpapManualFinding finding = PpapManualFinding.Create(
            tenantId,
            report.Id,
            report.SubmissionId,
            report.JobId,
            report.OwnerUserId,
            command.Message,
            command.EvidenceNote,
            actorId,
            DateTime.UtcNow,
            command.SubmissionDocumentId,
            command.CanonicalDocumentId,
            command.ExtractionAttemptId,
            document?.DocumentCode);
        dbContext.PpapManualFindings.Add(finding);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return finding.Id;
    }
}
