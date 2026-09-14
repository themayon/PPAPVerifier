using DES.Framework.Core.Exceptions;
using DES.Modules.Tickets.Contracts.Dtos;
using DES.Modules.Tickets.Contracts.v1.Tickets;
using DES.Modules.Tickets.Data;
using DES.Modules.Tickets.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Tickets.Features.v1.Tickets.GetTicketById;

public sealed class GetTicketByIdQueryHandler(TicketsDbContext dbContext)
    : IQueryHandler<GetTicketByIdQuery, TicketDto>
{
    public async ValueTask<TicketDto> Handle(GetTicketByIdQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var ticket = await dbContext.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == query.TicketId, cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null)
        {
            throw new NotFoundException($"Ticket {query.TicketId} not found.");
        }

        int commentCount = await dbContext.TicketComments
            .CountAsync(c => c.TicketId == ticket.Id, cancellationToken)
            .ConfigureAwait(false);

        return ticket.ToDto(commentCount);
    }
}
