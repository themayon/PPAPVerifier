using DES.Framework.Core.Exceptions;
using DES.Modules.Tickets.Contracts.v1.Tickets;
using DES.Modules.Tickets.Data;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace DES.Modules.Tickets.Features.v1.Tickets.ResolveTicket;

public sealed class ResolveTicketCommandHandler(TicketsDbContext dbContext)
    : ICommandHandler<ResolveTicketCommand, Guid>
{
    public async ValueTask<Guid> Handle(ResolveTicketCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var ticket = await dbContext.Tickets
            .FirstOrDefaultAsync(t => t.Id == command.TicketId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new NotFoundException($"Ticket {command.TicketId} not found.");

        ticket.Resolve(command.ResolutionNote);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return ticket.Id;
    }
}
