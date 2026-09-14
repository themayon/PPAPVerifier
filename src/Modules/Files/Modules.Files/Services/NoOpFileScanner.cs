using DES.Modules.Files.Domain;

namespace DES.Modules.Files.Services;

internal sealed class NoOpFileScanner : IFileScanner
{
    public ValueTask<ScanStatus> ScanAsync(string storageKey, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ScanStatus.Clean);
}
