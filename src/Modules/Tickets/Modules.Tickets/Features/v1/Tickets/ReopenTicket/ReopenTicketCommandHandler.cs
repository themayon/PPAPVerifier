using DES.Framework.Core.Exceptions;
using DES.Modules.Tickets.Contracts.v1.Tickets;
using DES.Modules.Tickets.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Tickets.Features.v1.Tickets.ReopenTicket;

public sealed class ReopenTicketCommandHandler(TicketsDbContext dbContext)
    : ICommandHandler<ReopenTicketCommand, Guid>
{
    public async ValueTask<Guid> Handle(ReopenTicketCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ticket = await dbContext.Tickets
            .FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Ticket {command.TicketId} not found.");

        ticket.Reopen();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ticket.Id;
    }
}
