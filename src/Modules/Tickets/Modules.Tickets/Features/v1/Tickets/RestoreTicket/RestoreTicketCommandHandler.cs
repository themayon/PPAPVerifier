using DES.Framework.Core.Exceptions;
using DES.Framework.Persistence;
using DES.Modules.Tickets.Contracts.v1.Tickets;
using DES.Modules.Tickets.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Tickets.Features.v1.Tickets.RestoreTicket;

public sealed class RestoreTicketCommandHandler(TicketsDbContext dbContext)
    : ICommandHandler<RestoreTicketCommand, Guid>
{
    public async ValueTask<Guid> Handle(RestoreTicketCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ticket = await dbContext.Tickets
            .IgnoreQueryFilters([QueryFilters.SoftDelete])
            .FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Ticket {command.TicketId} not found.");

        ticket.Restore();
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ticket.Id;
    }
}
