using Asp.Versioning;
using DES.Framework.Eventing;
using DES.Framework.Persistence;
using DES.Framework.Web.Modules;
using DES.Modules.Billing.Data;
using DES.Modules.Billing.Features.v1.Invoices.GenerateInvoices;
using DES.Modules.Billing.Features.v1.Invoices.GetInvoiceById;
using DES.Modules.Billing.Features.v1.Invoices.GetInvoices;
using DES.Modules.Billing.Features.v1.Invoices.GetInvoicePdf;
using DES.Modules.Billing.Features.v1.Invoices.GetMyInvoices;
using DES.Modules.Billing.Features.v1.Invoices.IssueInvoice;
using DES.Modules.Billing.Features.v1.Invoices.MarkInvoicePaid;
using DES.Modules.Billing.Features.v1.Invoices.VoidInvoice;
using DES.Modules.Billing.Features.v1.Plans.CreatePlan;
using DES.Modules.Billing.Features.v1.Plans.GetPlans;
using DES.Modules.Billing.Features.v1.Plans.UpdatePlan;
using DES.Modules.Billing.Features.v1.Subscriptions.AssignSubscription;
using DES.Modules.Billing.Features.v1.Subscriptions.GetSubscription;
using DES.Modules.Billing.Features.v1.Usage.CaptureUsageSnapshots;
using DES.Modules.Billing.Features.v1.Usage.GetUsageSnapshots;
using DES.Modules.Billing.Features.v1.Wallets.ApproveTopupRequest;
using DES.Modules.Billing.Features.v1.Wallets.CreateTopupRequest;
using DES.Modules.Billing.Features.v1.Wallets.GetMyTopupRequests;
using DES.Modules.Billing.Features.v1.Wallets.GetMyWallet;
using DES.Modules.Billing.Features.v1.Wallets.GetTopupRequests;
using DES.Modules.Billing.Features.v1.Wallets.RejectTopupRequest;
using DES.Modules.Billing.Services;
using Hangfire;
using Hangfire.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

[assembly: DesModule(typeof(DES.Modules.Billing.BillingModule), 500)]

namespace DES.Modules.Billing;

public sealed class BillingModule : IModule
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        DES.Framework.Shared.Constants.PermissionConstants.Register(
            DES.Modules.Billing.Contracts.Authorization.BillingPermissions.All);

        builder.Services.AddHeroDbContext<BillingDbContext>();
        builder.Services.AddScoped<IDbInitializer, BillingDbInitializer>();
        builder.Services.AddScoped<IUsageReporter, UsageReporter>();
        builder.Services.AddScoped<IBillingService, BillingService>();
        builder.Services.AddSingleton<IInvoicePdfRenderer, InvoicePdfRenderer>();

        // React to tenant create/renew events (Multitenancy.Contracts) to drive subscriptions + invoices.
        builder.Services.AddIntegrationEventHandlers(typeof(BillingModule).Assembly);

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<BillingDbContext>(
                name: "db:billing",
                failureStatus: HealthStatus.Unhealthy);
    }

    public void ConfigureMiddleware(IApplicationBuilder app)
    {
        // No custom middleware needed
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var versionSet = endpoints.NewApiVersionSet()
            .HasApiVersion(new ApiVersion(1))
            .ReportApiVersions()
            .Build();

        var group = endpoints
            .MapGroup("api/v{version:apiVersion}/billing")
            .WithTags("Billing")
            .WithApiVersionSet(versionSet)
            .RequireAuthorization();

        group.MapGetPlansEndpoint();
        group.MapCreatePlanEndpoint();
        group.MapUpdatePlanEndpoint();

        group.MapGetSubscriptionEndpoint();
        group.MapGetMySubscriptionEndpoint();
        group.MapAssignSubscriptionEndpoint();

        group.MapGetInvoicesEndpoint();
        group.MapGetMyInvoicesEndpoint();
        group.MapGetInvoiceByIdEndpoint();
        group.MapGetInvoicePdfEndpoint();
        group.MapGenerateInvoicesEndpoint();
        group.MapIssueInvoiceEndpoint();
        group.MapMarkInvoicePaidEndpoint();
        group.MapVoidInvoiceEndpoint();

        group.MapGetUsageSnapshotsEndpoint();
        group.MapCaptureUsageSnapshotsEndpoint();

        group.MapGetMyWalletEndpoint();
        group.MapCreateTopupRequestEndpoint();
        group.MapGetMyTopupRequestsEndpoint();

        group.MapGetTopupRequestsEndpoint();
        group.MapApproveTopupRequestEndpoint();
        group.MapRejectTopupRequestEndpoint();

        var jobManager = endpoints.ServiceProvider.GetService<IRecurringJobManager>();
        if (jobManager is not null)
        {
            // Fire at 00:05 UTC on the 1st of every month; the job bills the previous period.
            jobManager.AddOrUpdate(
                "billing-monthly-invoices",
                Job.FromExpression<MonthlyInvoiceJob>(j => j.RunAsync(CancellationToken.None)),
                "5 0 1 * *",
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }
    }
}
