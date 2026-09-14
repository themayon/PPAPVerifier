using DES.Framework.Shared.Persistence;
using DES.Modules.Tickets.Contracts.Dtos;
using Mediator;

namespace DES.Modules.Tickets.Contracts.v1.Tickets;

public sealed record ListTrashedTicketsQuery(int PageNumber = 1, int PageSize = 20)
    : IQuery<PagedResponse<TicketDto>>;
