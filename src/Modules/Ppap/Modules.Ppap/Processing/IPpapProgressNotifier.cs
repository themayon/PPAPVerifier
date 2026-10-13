using DES.Modules.Ppap.Domain;

namespace DES.Modules.Ppap.Processing;

public interface IPpapProgressNotifier
{
    Task NotifyAsync(PpapJob job, CancellationToken cancellationToken = default);
}
