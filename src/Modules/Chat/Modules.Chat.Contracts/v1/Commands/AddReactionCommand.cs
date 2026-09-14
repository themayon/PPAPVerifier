using Mediator;

namespace DES.Modules.Chat.Contracts.v1.Commands;

public sealed record AddReactionCommand(Guid MessageId, string Emoji) : ICommand<Unit>;
