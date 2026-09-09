using Mediator;

namespace DES.Modules.Notifications.Contracts.v1.Commands;

public sealed record MarkNotificationReadCommand(Guid NotificationId) : ICommand<Unit>;
