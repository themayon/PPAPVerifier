using System.Net;
using DES.Framework.Core.Exceptions;
using DES.Framework.Shared.Storage;
using DES.Framework.Storage;
using DES.Framework.Storage.Services;
using DES.Modules.Files.Contracts;
using DES.Modules.Files.Contracts.v1.DTOs;
using DES.Modules.Files.Data;
using DES.Modules.Files.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DES.Modules.Files.Services;

internal sealed class InternalFileArtifactService(
    FilesDbContext db,
    IStorageService storage,
    FileAccessPolicyRegistry policies,
    IFileScanner scanner,
    IOptions<FilesOptions> options,
    ILogger<InternalFileArtifactService> logger) : IInternalFileArtifactService
{
    private const string CategoryName = "GeneratedArtifact";

    public async Task<InternalFileArtifactContent> OpenReadAsync(
        Guid fileAssetId,
        string currentUserId,
        CancellationToken ct = default)
    {
        if (fileAssetId == Guid.Empty)
        {
            throw new ArgumentException("A file asset identifier is required.", nameof(fileAssetId));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUserId);

        FileAsset asset = await db.FileAssets
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == fileAssetId, ct)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("file not found");

        if (asset.Status != FileAssetStatus.Available)
        {
            throw new NotFoundException("file not found");
        }

        IFileAccessPolicy policy = policies.Resolve(asset.OwnerType)
            ?? throw new ForbiddenException("no file access policy registered for this owner type");
        FileAccessContext context = new(
            asset.Id,
            asset.OwnerType,
            asset.OwnerId,
            asset.CreatedByUserId,
            (int)asset.Visibility);
        if (!await policy.CanReadAsync(context, currentUserId, ct).ConfigureAwait(false))
        {
            throw new ForbiddenException("not allowed to read this file");
        }

        var download = await storage.DownloadAsync(asset.StorageKey, ct).ConfigureAwait(false)
            ?? throw new NotFoundException("file content not found");
        return new InternalFileArtifactContent(
            download.Stream,
            asset.ContentType,
            asset.OriginalFileName,
            asset.SizeBytes);
    }

    public async Task<GeneratedFileArtifactResult> CreatePrivateAsync(
        GeneratedFileArtifactRequest request,
        string currentUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwnerType);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContentType);
        ArgumentNullException.ThrowIfNull(request.Data);
        if (request.OwnerId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty owner identifier is required.", nameof(request));
        }
        if (request.Data.Length == 0)
        {
            throw new ArgumentException("Artifact data is required.", nameof(request));
        }

        FileCategoryOptions category = options.Value.Categories.TryGetValue(CategoryName, out FileCategoryOptions? configured)
            ? configured
            : throw new InvalidOperationException($"Files category '{CategoryName}' is not configured.");
        string originalFileName = Path.GetFileName(request.FileName);
        string extension = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(originalFileName)
            || string.IsNullOrWhiteSpace(extension)
            || !category.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new CustomException("Artifact file extension is not allowed.", (IEnumerable<string>?)null, HttpStatusCode.BadRequest);
        }
        if (request.Data.LongLength > category.MaxBytes)
        {
            throw new CustomException(
                $"Artifact exceeds max size of {category.MaxBytes} bytes.",
                (IEnumerable<string>?)null,
                HttpStatusCode.BadRequest);
        }

        IFileAccessPolicy policy = policies.Resolve(request.OwnerType)
            ?? throw new ForbiddenException("no file access policy registered for this owner type");
        if (!await policy.CanAttachAsync(request.OwnerId, currentUserId, ct).ConfigureAwait(false))
        {
            throw new ForbiddenException("not allowed to attach files to this owner");
        }

        string sanitizedFileName = StorageKeyBuilder.Sanitize(originalFileName);
        string storedFileName = string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(sanitizedFileName, ".txt")
            : sanitizedFileName;
        FileUploadRequest uploadRequest = new()
        {
            FileName = storedFileName,
            ContentType = request.ContentType,
            Data = [.. request.Data]
        };
        string storageKey = await storage.UploadAsync<GeneratedArtifactStorageMarker>(
            uploadRequest,
            FileType.Document,
            ct).ConfigureAwait(false);

        try
        {
            Guid id = Guid.CreateVersion7();
            FileAsset asset = FileAsset.CreatePending(
                id,
                request.OwnerType,
                request.OwnerId,
                storedFileName,
                storedFileName,
                request.ContentType,
                request.Data.LongLength,
                storageKey,
                Visibility.Private,
                currentUserId,
                DateTimeOffset.UtcNow.AddMinutes(options.Value.UploadUrlTtlMinutes));
            ScanStatus scanStatus = await scanner.ScanAsync(storageKey, ct).ConfigureAwait(false);
            asset.MarkAvailable(request.Data.LongLength, scanStatus);
            db.FileAssets.Add(asset);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return new GeneratedFileArtifactResult(
                asset.Id,
                asset.Status,
                (int)asset.ScanStatus,
                asset.SizeBytes);
        }
        catch
        {
            try
            {
                await storage.RemoveAsync(storageKey, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                logger.LogWarning(cleanupException, "Failed to remove generated artifact after creation failure");
            }
            throw;
        }
    }

    public async Task<int> SoftDeleteAsync(
        IReadOnlyCollection<Guid> fileAssetIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fileAssetIds);
        Guid[] ids = fileAssetIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (ids.Length == 0)
        {
            return 0;
        }

        List<FileAsset> assets = await db.FileAssets
            .Where(asset => ids.Contains(asset.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (assets.Count == 0)
        {
            return 0;
        }

        db.FileAssets.RemoveRange(assets);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return assets.Count;
    }

    private sealed record GeneratedArtifactStorageMarker(Guid Id);
}
