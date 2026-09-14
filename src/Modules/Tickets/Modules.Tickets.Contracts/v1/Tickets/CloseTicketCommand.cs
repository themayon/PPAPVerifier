using Mediator;

namespace DES.Modules.Tickets.Contracts.v1.Tickets;

public sealed record CloseTicketCommand(Guid TicketId) : ICommand<Guid>;
