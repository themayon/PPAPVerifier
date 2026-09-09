using FluentValidation;
using DES.Modules.Notifications.Contracts.v1.Queries;

namespace DES.Modules.Notifications.Features.v1.ListNotifications;

public sealed class ListNotificationsQueryValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}
