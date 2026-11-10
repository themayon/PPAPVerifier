using DES.Modules.Files.Contracts;
using DES.Modules.Ppap.Data;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Ppap.Authorization;

public sealed class PpapSupplierCertificateFileAccessPolicy(PpapDbContext dbContext) : IFileAccessPolicy
{
    public const string OwnerTypeName = "PpapSupplierCertificate";

    public string OwnerType => OwnerTypeName;

    public async Task<bool> CanAttachAsync(Guid? ownerId, string currentUserId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(currentUserId, out _)) return false;
        if (ownerId is not { } supplierId) return true;
        return await dbContext.PpapSuppliers.AsNoTracking().AnyAsync(x => x.Id == supplierId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> CanReadAsync(FileAccessContext context, string currentUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsUploader(context, currentUserId)) return true;
        return await dbContext.PpapSuppliers.AsNoTracking()
            .AnyAsync(x => x.Certificates.Any(c => c.FileAssetId == context.FileAssetId), cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> CanDeleteAsync(FileAccessContext context, string currentUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult(IsUploader(context, currentUserId));
    }

    public Task<bool> CanChangeVisibilityAsync(FileAccessContext context, string currentUserId, CancellationToken cancellationToken) => Task.FromResult(false);

    private static bool IsUploader(FileAccessContext context, string currentUserId) =>
        Guid.TryParse(currentUserId, out Guid userId) && Guid.TryParse(context.CreatedByUserId, out Guid uploaderId) && userId == uploaderId;
}
