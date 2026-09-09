using Mediator;

namespace DES.Modules.Tickets.Contracts.v1.Tickets;

public sealed record AddTicketCommentCommand(Guid TicketId, string Body) : ICommand<Guid>;
