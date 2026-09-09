using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Billing.Contracts.Authorization;
using DES.Modules.Billing.Contracts.v1.Wallets;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Billing.Features.v1.Wallets.GetMyWallet;

public static class GetMyWalletEndpoint
{
    internal static RouteHandlerBuilder MapGetMyWalletEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapGet("/wallet/me",
                async (IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(new GetMyWalletQuery(), ct)))
            .WithName("GetMyWallet")
            .WithSummary("Get the wallet for the current tenant")
            .RequirePermission(BillingPermissions.View);
    }
}
