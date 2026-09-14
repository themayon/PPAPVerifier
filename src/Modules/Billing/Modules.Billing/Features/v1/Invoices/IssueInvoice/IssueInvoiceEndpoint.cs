using DES.Modules.Billing.Contracts.Authorization;
using DES.Framework.Shared.Identity.Authorization;
using DES.Framework.Web.Idempotency;
using DES.Modules.Billing.Contracts.v1.Invoices;
using Mediator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DES.Modules.Billing.Features.v1.Invoices.IssueInvoice;

public static class IssueInvoiceEndpoint
{
    public sealed record IssueInvoiceBody(DateTime? DueAtUtc);

    internal static RouteHandlerBuilder MapIssueInvoiceEndpoint(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/invoices/{invoiceId:guid}/issue",
                async (Guid invoiceId, IssueInvoiceBody? body, IMediator mediator, CancellationToken ct) =>
                    Results.Ok(await mediator.Send(new IssueInvoiceCommand(invoiceId, body?.DueAtUtc), ct)))
            .WithName("IssueInvoice")
            .WithSummary("Issue a draft invoice")
            .RequirePermission(BillingPermissions.Manage)
            .WithIdempotency();
    }
}
