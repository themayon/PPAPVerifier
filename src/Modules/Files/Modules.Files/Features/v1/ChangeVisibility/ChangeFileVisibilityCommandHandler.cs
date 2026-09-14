using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Framework.Storage.Services;
using DES.Modules.Files.Contracts;
using DES.Modules.Files.Contracts.v1.Commands;
using DES.Modules.Files.Contracts.v1.DTOs;
using DES.Modules.Files.Data;
using DES.Modules.Files.Domain;
using DES.Modules.Files.Features.v1.Internal;
using DES.Modules.Files.Services;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Files.Features.v1.ChangeVisibility;

public sealed class ChangeFileVisibilityCommandHandler(
    FilesDbContext db,
    FileAccessPolicyRegistry policies,
    ICurrentUser currentUser,
    IStorageService storage)
    : ICommandHandler<ChangeFileVisibilityCommand, FileAssetDto>
{
    public async ValueTask<FileAssetDto> Handle(ChangeFileVisibilityCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);

        if (cmd.Visibility is not (Visibility.Public or Visibility.Private))
        {
            throw new CustomException(
                $"Unknown visibility value '{cmd.Visibility}'.",
                errors: null,
                System.Net.HttpStatusCode.BadRequest);
        }

        var f = await db.FileAssets
            .FirstOrDefaultAsync(x => x.Id == cmd.FileAssetId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("file not found");

        var userId = currentUser.GetUserId().ToString();
        var policy = policies.Resolve(f.OwnerType)
            ?? throw new ForbiddenException("no policy");
        var ctx = new FileAccessContext(f.Id, f.OwnerType, f.OwnerId, f.CreatedByUserId, (int)f.Visibility);
        if (!await policy.CanChangeVisibilityAsync(ctx, userId, cancellationToken).ConfigureAwait(false))
        {
            throw new ForbiddenException("not allowed to change this file's visibility");
        }

        f.ChangeVisibility(cmd.Visibility);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var publicUrl = f.Visibility == Visibility.Public
            ? storage.BuildPublicUrl(f.StorageKey)
            : null;
        return FileAssetMapper.ToDto(f, publicUrl);
    }
}
