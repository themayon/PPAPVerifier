using Asp.Versioning;
using DES.Framework.Persistence;
using DES.Framework.Shared.Constants;
using DES.Framework.Web.HttpResilience;
using DES.Framework.Web.Modules;
using DES.Modules.Webhooks.Contracts.Authorization;
using DES.Modules.Webhooks.Data;
using DES.Modules.Webhooks.Features.v1.CreateWebhookSubscription;
using DES.Modules.Webhooks.Features.v1.DeleteWebhookSubscription;
using DES.Modules.Webhooks.Features.v1.GetWebhookDeliveries;
using DES.Modules.Webhooks.Features.v1.GetWebhookSubscriptions;
using DES.Modules.Webhooks.Features.v1.TestWebhookSubscription;
using DES.Framework.Eventing.Abstractions;
using DES.Modules.Webhooks.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using System.Net.Http;

[assembly: DesModule(typeof(DES.Modules.Webhooks.WebhooksModule), 400)]

namespace DES.Modules.Webhooks;

public sealed class WebhooksModule : IModule
{
    public void ConfigureServices(IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        PermissionConstants.Register(WebhooksPermissions.All);

        builder.Services.AddHeroDbContext<WebhookDbContext>();
        builder.Services.AddScoped<IDbInitializer, WebhookDbInitializer>();
        builder.Services.AddSingleton<IWebhookSecretProtector, WebhookSecretProtector>();
        builder.Services.AddScoped<IWebhookDeliveryService, WebhookDeliveryService>();
        builder.Services.AddScoped<IWebhookDispatcher, WebhookDispatcher>();
        builder.Services.AddScoped<WebhookDispatchJob>();

        // Open-generic integration-event bridge — every published IIntegrationEvent fans out to
        // matching tenant webhook subscriptions; DI materializes closed handler types per event.
        builder.Services.AddScoped(
            typeof(IIntegrationEventHandler<>),
            typeof(WebhookFanoutHandler<>));

        builder.Services.AddHttpClient("Webhooks")
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // Untrusted tenant-supplied destination: never follow redirects (a 302 could bounce
                // to an internal host) and screen the resolved IP at connect time so DNS-rebinding
                // cannot map a public hostname to an internal address after the create-time check.
                AllowAutoRedirect = false,
                ConnectCallback = WebhookUrlGuard.ConnectAsync,
            })
            .AddHeroResilience(builder.Configuration);

        builder.Services.AddHealthChecks()
            .AddDbContextCheck<WebhookDbContext>(
                name: "db:webhooks",
                failureStatus: HealthStatus.Unhealthy);
    }

    public void ConfigureMiddleware(Microsoft.AspNetCore.Builder.IApplicationBuilder app)
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
            .MapGroup("api/v{version:apiVersion}/webhooks")
            .WithTags("Webhooks")
            .WithApiVersionSet(versionSet)
            .RequireAuthorization();

        group.MapCreateWebhookSubscriptionEndpoint();
        group.MapDeleteWebhookSubscriptionEndpoint();
        group.MapGetWebhookSubscriptionsEndpoint();
        group.MapGetWebhookDeliveriesEndpoint();
        group.MapTestWebhookSubscriptionEndpoint();
    }
}
