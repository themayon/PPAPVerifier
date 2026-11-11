using DES.Modules.Files.Contracts.v1.DTOs;

namespace DES.Modules.Files.Contracts;

public interface IInternalFileArtifactService
{
    Task<InternalFileArtifactContent> OpenReadAsync(
        Guid fileAssetId,
        string currentUserId,
        CancellationToken ct = default);

    Task<GeneratedFileArtifactResult> CreatePrivateAsync(
        GeneratedFileArtifactRequest request,
        string currentUserId,
        CancellationToken ct = default);

    Task<int> SoftDeleteAsync(
        IReadOnlyCollection<Guid> fileAssetIds,
        CancellationToken ct = default);
}

public sealed record GeneratedFileArtifactRequest(
    string OwnerType,
    Guid OwnerId,
    string FileName,
    string ContentType,
    byte[] Data);

public sealed record GeneratedFileArtifactResult(
    Guid FileAssetId,
    FileAssetStatus FileAssetStatus,
    int ScanStatus,
    long SizeBytes);

public sealed class InternalFileArtifactContent : IDisposable, IAsyncDisposable
{
    public InternalFileArtifactContent(Stream content, string contentType, string fileName, long sizeBytes)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(sizeBytes);

        Content = content;
        ContentType = contentType;
        FileName = fileName;
        SizeBytes = sizeBytes;
    }

    public Stream Content { get; }
    public string ContentType { get; }
    public string FileName { get; }
    public long SizeBytes { get; }

    public void Dispose() => Content.Dispose();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
