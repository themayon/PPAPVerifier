using Mediator;

namespace DES.Modules.Chat.Contracts.v1.Commands;

public sealed record DeleteMessageCommand(Guid MessageId) : ICommand<Unit>;
