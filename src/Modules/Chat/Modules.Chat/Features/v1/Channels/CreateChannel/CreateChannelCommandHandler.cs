using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Chat.Contracts.v1.Commands;
using DES.Modules.Chat.Data;
using DES.Modules.Chat.Domain;
using Mediator;

namespace DES.Modules.Chat.Features.v1.Channels.CreateChannel;

public sealed class CreateChannelCommandHandler(
    ChatDbContext db,
    ICurrentUser currentUser)
    : ICommandHandler<CreateChannelCommand, Guid>
{
    public async ValueTask<Guid> Handle(CreateChannelCommand cmd, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        var userId = currentUser.GetUserId().ToString();
        if (userId == Guid.Empty.ToString())
        {
            throw new UnauthorizedException("no current user");
        }

        var channel = ChatChannel.CreateChannel(cmd.Name, cmd.Description, cmd.IsPrivate, userId);
        db.Channels.Add(channel);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return channel.Id;
    }
}
