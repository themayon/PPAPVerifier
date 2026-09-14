using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Storage.Services;
using DES.Modules.Files.Contracts;
using DES.Modules.Files.Contracts.v1.DTOs;
using DES.Modules.Files.Contracts.v1.Queries;
using DES.Modules.Files.Data;
using DES.Modules.Files.Domain;
using DES.Modules.Files.Features.v1.Internal;
using DES.Modules.Files.Services;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Files.Features.v1.GetFileMetadata;

public sealed class GetFileMetadataQueryHandler(
    FilesDbContext db,
    FileAccessPolicyRegistry policies,
    ICurrentUser currentUser,
    IStorageService storage)
    : IQueryHandler<GetFileMetadataQuery, FileAssetDto>
{
    public async ValueTask<FileAssetDto> Handle(GetFileMetadataQuery q, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(q);

        var f = await db.FileAssets.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == q.FileAssetId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("file not found");

        var userId = currentUser.GetUserId().ToString();
        var policy = policies.Resolve(f.OwnerType)
            ?? throw new NotFoundException("file not found"); // don't leak existence on missing policy

        var ctx = new FileAccessContext(f.Id, f.OwnerType, f.OwnerId, f.CreatedByUserId, (int)f.Visibility);
        if (!await policy.CanReadAsync(ctx, userId, cancellationToken).ConfigureAwait(false))
        {
            throw new NotFoundException("file not found");
        }

        // Public files get a durable URL safe to persist long-term, while private files mint a
        // short-lived presigned GET on demand via the auth-gated url endpoint.
        var publicUrl = f.Visibility == Visibility.Public
            ? storage.BuildPublicUrl(f.StorageKey)
            : null;

        return FileAssetMapper.ToDto(f, publicUrl);
    }
}
