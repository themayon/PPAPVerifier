using FluentValidation;
using DES.Modules.Tickets.Contracts.v1.Tickets;

namespace DES.Modules.Tickets.Features.v1.Tickets.CreateTicket;

public sealed class CreateTicketCommandValidator : AbstractValidator<CreateTicketCommand>
{
    public CreateTicketCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(160);
        RuleFor(x => x.Description).MaximumLength(4096);
    }
}
