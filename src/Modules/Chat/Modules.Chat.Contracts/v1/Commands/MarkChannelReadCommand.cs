using Mediator;

namespace DES.Modules.Chat.Contracts.v1.Commands;

public sealed record MarkChannelReadCommand(Guid ChannelId, Guid MessageId) : ICommand<Unit>;
