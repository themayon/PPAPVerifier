using DES.Modules.Billing.Contracts.Authorization;
using DES.Framework.Shared.Identity.Authorization;
using DES.Modules.Billing.Contracts.v1.Invoices;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Billing.Features.v1.Invoices.VoidInvoice;

public static class VoidInvoiceEndpoint
{
    public sealed record VoidInvoiceBody(string? Reason);

    internal static RouteHandlerBuilder MapVoidInvoiceEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/invoices/{invoiceId:guid}/void",
                async (Guid invoiceId, VoidInvoiceBody? body, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(new VoidInvoiceCommand(invoiceId, body?.Reason), ct)))
            .WithName("VoidInvoice")
            .WithSummary("Void an invoice")
            .RequirePermission(BillingPermissions.Manage);
    }
}
