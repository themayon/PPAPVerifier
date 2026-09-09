using DES.Framework.Core.Domain;

namespace DES.Modules.Tickets.Domain.Events;

public sealed record TicketCommentAddedDomainEvent(
    Guid TicketId,
    Guid CommentId,
    Guid AuthorUserId,
    Guid EventId,
    DateTimeOffset OccurredOnUtc) : DomainEvent(EventId, OccurredOnUtc);
