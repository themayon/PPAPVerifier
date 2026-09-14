using DES.Framework.Core.Domain;
using DES.Modules.Tickets.Contracts.Dtos;

namespace DES.Modules.Tickets.Domain.Events;

public sealed record TicketCreatedDomainEvent(
    Guid TicketId,
    string Number,
    string Title,
    TicketPriority Priority,
    Guid ReporterUserId,
    Guid? AssignedToUserId,
    Guid EventId,
    DateTimeOffset OccurredOnUtc) : DomainEvent(EventId, OccurredOnUtc);
