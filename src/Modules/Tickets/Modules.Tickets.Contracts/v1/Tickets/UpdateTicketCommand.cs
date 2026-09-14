using DES.Modules.Tickets.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Tickets.Contracts.v1.Tickets;

public sealed record UpdateTicketCommand(
    Guid TicketId,
    string Title,
    string? Description = null,
    TicketPriority Priority = TicketPriority.Medium) : ICommand<Guid>;
