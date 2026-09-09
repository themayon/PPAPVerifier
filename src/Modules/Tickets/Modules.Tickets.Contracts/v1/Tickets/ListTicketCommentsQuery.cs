using DES.Modules.Tickets.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Tickets.Contracts.v1.Tickets;

public sealed record ListTicketCommentsQuery(Guid TicketId) : IQuery<IReadOnlyList<TicketCommentDto>>;
