using Mediator;

namespace DES.Modules.Tickets.Contracts.v1.Tickets;

public sealed record DeleteTicketCommand(Guid TicketId) : ICommand<Unit>;
