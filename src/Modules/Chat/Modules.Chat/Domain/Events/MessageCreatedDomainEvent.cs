using DES.Framework.Core.Domain;

namespace DES.Modules.Chat.Domain.Events;

public sealed record MessageCreatedDomainEvent(
    Guid ChannelId,
    Guid MessageId,
    string AuthorUserId,
    Guid? ParentMessageId,
    Guid EventId,
    DateTimeOffset OccurredOnUtc) : DomainEvent(EventId, OccurredOnUtc);
