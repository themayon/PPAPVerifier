using FluentValidation;
using DES.Modules.Chat.Contracts.v1.Queries;

namespace DES.Modules.Chat.Features.v1.Channels.DiscoverChannels;

public sealed class DiscoverChannelsQueryValidator : AbstractValidator<DiscoverChannelsQuery>
{
    public DiscoverChannelsQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Search).MaximumLength(200).When(x => x.Search is not null);
    }
}
