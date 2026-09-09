using FluentValidation;
using DES.Modules.Tickets.Contracts.v1.Tickets;

namespace DES.Modules.Tickets.Features.v1.Tickets.AddTicketComment;

public sealed class AddTicketCommentCommandValidator : AbstractValidator<AddTicketCommentCommand>
{
    public AddTicketCommentCommandValidator()
    {
        RuleFor(x => x.Body).NotEmpty().MaximumLength(8192);
    }
}
