using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Files.Contracts.Authorization;
using DES.Modules.Files.Contracts.v1.Queries;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Files.Features.v1.ListTrashedFiles;

public static class ListTrashedFilesEndpoint
{
    internal static RouteHandlerBuilder MapListTrashedFilesEndpoint(this IEndpointRouteBuilder endpoints)
        => endpoints.MapGet("/trash",
                async (int? pageNumber, int? pageSize, IMediator mediator, CancellationToken cancellationToken) =>
                    Results.Ok(await mediator.Send(new ListTrashedFilesQuery(pageNumber ?? 1, pageSize ?? 20), cancellationToken)))
            .WithName("ListTrashedFiles")
            .WithSummary("List soft-deleted files (admin/trash view)")
            .RequirePermission(FilesPermissions.ViewTrash);
}
