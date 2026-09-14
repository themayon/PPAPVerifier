using FluentValidation;
using DES.Modules.Chat.Contracts.v1.Queries;

namespace DES.Modules.Chat.Features.v1.Messages.ListMessageReplies;

public sealed class ListMessageRepliesQueryValidator : AbstractValidator<ListMessageRepliesQuery>
{
    public ListMessageRepliesQueryValidator()
    {
        RuleFor(x => x.ParentMessageId).NotEmpty();
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}
