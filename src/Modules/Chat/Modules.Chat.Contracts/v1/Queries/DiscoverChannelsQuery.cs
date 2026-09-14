using System.Collections.ObjectModel;
using DES.Modules.Chat.Contracts.v1.DTOs;
using Mediator;

namespace DES.Modules.Chat.Contracts.v1.Queries;

public sealed record DiscoverChannelsQuery(string? Search, int Page = 1, int PageSize = 50)
    : IQuery<ReadOnlyCollection<ChannelDto>>;
