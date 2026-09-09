using FluentValidation;
using DES.Modules.Chat.Contracts.v1.Commands;

namespace DES.Modules.Chat.Features.v1.Channels.MarkChannelRead;

public sealed class MarkChannelReadCommandValidator : AbstractValidator<MarkChannelReadCommand>
{
    public MarkChannelReadCommandValidator()
    {
        RuleFor(x => x.ChannelId).NotEmpty();
        RuleFor(x => x.MessageId).NotEmpty();
    }
}
