using DES.Modules.Tickets.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Tickets.Contracts.v1.Tickets;

public sealed record GetTicketByIdQuery(Guid TicketId) : IQuery<TicketDto>;
