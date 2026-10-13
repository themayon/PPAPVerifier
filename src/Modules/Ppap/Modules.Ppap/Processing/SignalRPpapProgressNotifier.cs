using DES.Framework.Web.Realtime;
using DES.Modules.Ppap.Domain;
using Microsoft.AspNetCore.SignalR;

namespace DES.Modules.Ppap.Processing;

public sealed class SignalRPpapProgressNotifier(IHubContext<AppHub> hub) : IPpapProgressNotifier
{
    public Task NotifyAsync(PpapJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        return hub.Clients.Group($"user:{job.OwnerUserId}")
            .SendAsync("PpapJobProgressed", new
            {
                jobId = job.Id,
                submissionId = job.SubmissionId,
                status = job.Status.ToString(),
                currentStage = job.CurrentStage.ToString(),
                progressPercent = job.ProgressPercent,
                updatedAtUtc = DateTime.UtcNow
            }, cancellationToken);
    }
}
