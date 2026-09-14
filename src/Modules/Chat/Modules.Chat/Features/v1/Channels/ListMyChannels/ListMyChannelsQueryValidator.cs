using FluentValidation;
using DES.Modules.Chat.Contracts.v1.Queries;

namespace DES.Modules.Chat.Features.v1.Channels.ListMyChannels;

public sealed class ListMyChannelsQueryValidator : AbstractValidator<ListMyChannelsQuery>
{
    public ListMyChannelsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}
