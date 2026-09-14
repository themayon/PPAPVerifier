using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Files.Contracts.Authorization;
using DES.Modules.Files.Contracts.v1.Commands;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Files.Features.v1.RequestUploadUrl;

public static class RequestUploadUrlEndpoint
{
    internal static RouteHandlerBuilder MapRequestUploadUrlEndpoint(this IEndpointRouteBuilder endpoints)
        => endpoints.MapPost("/upload-url",
                async (RequestUploadUrlCommand command, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(command, ct)))
            .WithName("RequestFileUploadUrl")
            .WithSummary("Mint a presigned PUT URL for a file upload")
            .RequirePermission(FilesPermissions.Upload)
            .WithIdempotency();
}
