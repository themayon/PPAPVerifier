using DES.Framework.Core.Domain;

namespace DES.Modules.Tickets.Domain.Events;

public sealed record TicketAssignedDomainEvent(
    Guid TicketId,
    Guid? PreviousAssigneeUserId,
    Guid? NewAssigneeUserId,
    Guid EventId,
    DateTimeOffset OccurredOnUtc) : DomainEvent(EventId, OccurredOnUtc);
