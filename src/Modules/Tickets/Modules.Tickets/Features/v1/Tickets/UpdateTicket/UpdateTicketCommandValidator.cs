using FluentValidation;
using DES.Modules.Tickets.Contracts.v1.Tickets;

namespace DES.Modules.Tickets.Features.v1.Tickets.UpdateTicket;

public sealed class UpdateTicketCommandValidator : AbstractValidator<UpdateTicketCommand>
{
    public UpdateTicketCommandValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Description).MaximumLength(4096);
        RuleFor(x => x.Priority).IsInEnum();
    }
}
