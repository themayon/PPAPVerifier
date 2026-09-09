using FluentValidation;
using DES.Modules.Chat.Contracts.v1.Commands;

namespace DES.Modules.Chat.Features.v1.Messages.EditMessage;

public sealed class EditMessageCommandValidator : AbstractValidator<EditMessageCommand>
{
    public EditMessageCommandValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty();
        RuleFor(x => x.Body).NotEmpty().MaximumLength(32_768);
    }
}
