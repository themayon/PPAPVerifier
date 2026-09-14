using DES.Framework.Shared.Persistence;
using DES.Modules.Files.Contracts.v1.DTOs;
using Mediator;

namespace DES.Modules.Files.Contracts.v1.Queries;

public sealed record ListTrashedFilesQuery(int PageNumber = 1, int PageSize = 20)
    : IQuery<PagedResponse<FileAssetDto>>;
