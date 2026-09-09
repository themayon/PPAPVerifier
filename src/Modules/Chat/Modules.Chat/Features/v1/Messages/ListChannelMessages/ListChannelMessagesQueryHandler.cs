using System.Collections.ObjectModel;
using DES.Framework.Core.Context;
using DES.Framework.Core.Exceptions;
using DES.Modules.Chat.Contracts.v1.DTOs;
using DES.Modules.Chat.Contracts.v1.Queries;
using DES.Modules.Chat.Data;
using DES.Modules.Chat.Features.v1.Internal;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Chat.Features.v1.Messages.ListChannelMessages;

public sealed class ListChannelMessagesQueryHandler(
    ChatDbContext db,
    ICurrentUser currentUser,
    IMediator mediator)
    : IQueryHandler<ListChannelMessagesQuery, ReadOnlyCollection<MessageDto>>
{
    public async ValueTask<ReadOnlyCollection<MessageDto>> Handle(
        ListChannelMessagesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var userId = currentUser.GetUserId();
        if (userId == Guid.Empty) throw new UnauthorizedException("no current user");
        var currentUserId = userId.ToString();

        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == query.ChannelId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException("Channel not found.");
        channel.RequireMember(currentUserId);

        // Top-level only (no thread replies). Guid v7 monotonic → Id desc = time desc.
        IQueryable<Domain.Message> q = db.Messages
            .Where(m => m.ChannelId == query.ChannelId && m.ParentMessageId == null);

        if (query.Before is { } beforeId)
        {
            q = q.Where(m => m.Id.CompareTo(beforeId) < 0);
        }

        var rows = await q
            .OrderByDescending(m => m.Id)
            .Take(query.PageSize)
            .Include(m => m.Attachments)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var dtos = rows.Select(m => m.ToDto()).ToList();
        var resolved = await ChatAttachmentUrls.ResolveAsync(dtos, mediator, cancellationToken).ConfigureAwait(false);
        return resolved.AsReadOnly();
    }
}
