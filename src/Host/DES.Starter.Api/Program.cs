using DES.Framework.Eventing;
using DES.Framework.Web;
using DES.Framework.Web.Modules;
using DES.Modules.Auditing;
using DES.Modules.Identity;
using DES.Modules.Identity.Contracts.v1.Tokens.TokenGeneration;
using DES.Modules.Identity.Features.v1.Tokens.TokenGeneration;
using DES.Modules.Multitenancy;
using DES.Modules.Multitenancy.Contracts.v1.GetTenantStatus;
using DES.Modules.Webhooks;
using DES.Modules.Billing;
using DES.Modules.Catalog;
using DES.Modules.Tickets;
using DES.Modules.Multitenancy.Features.v1.GetTenantStatus;
using System.Reflection;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Serialize enums as string names (reads still accept names or integers). [Flags] enums (AuditTag, BodyCapture)
// opt back to numeric via their own NumericEnumConverter since comma-joined flag strings break bitwise consumers. Frontends mirror this as string unions.
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

if (builder.Environment.IsProduction())
{
    static void Require(IConfiguration config, string key)
    {
        if (string.IsNullOrWhiteSpace(config[key]))
        {
            throw new InvalidOperationException($"Missing required configuration '{key}' in Production.");
        }
    }

    var config = builder.Configuration;
    Require(config, "DatabaseOptions:ConnectionString");
    Require(config, "CachingOptions:Redis");
    Require(config, "JwtOptions:SigningKey");
}

builder.Services.AddMediator(o =>
{
    o.ServiceLifetime = ServiceLifetime.Scoped;
    o.Assemblies = [
        typeof(GenerateTokenCommand),
        typeof(GenerateTokenCommandHandler),
        typeof(GetTenantStatusQuery),
        typeof(GetTenantStatusQueryHandler),
        typeof(DES.Modules.Auditing.Contracts.AuditEnvelope),
        typeof(DES.Modules.Auditing.Persistence.AuditDbContext),
        typeof(DES.Modules.Webhooks.Contracts.v1.CreateWebhookSubscription.CreateWebhookSubscriptionCommand),
        typeof(DES.Modules.Webhooks.WebhooksModule),
        typeof(DES.Modules.Billing.Contracts.BillingContractsMarker),
        typeof(DES.Modules.Billing.BillingModule),
        typeof(DES.Modules.Catalog.Contracts.CatalogContractsMarker),
        typeof(DES.Modules.Catalog.CatalogModule),
        typeof(DES.Modules.Tickets.Contracts.TicketsContractsMarker),
        typeof(DES.Modules.Tickets.TicketsModule),
        typeof(DES.Modules.Rules.Contracts.RulesContractsMarker),
        typeof(DES.Modules.Rules.RulesModule),
        typeof(DES.Modules.Files.Contracts.v1.Commands.RequestUploadUrlCommand),
        typeof(DES.Modules.Files.FilesModule),
        typeof(DES.Modules.Ppap.Contracts.PpapContractsMarker),
        typeof(DES.Modules.Ppap.PpapModule),
        typeof(DES.Modules.DocumentIntelligence.Contracts.DocumentIntelligenceContractsMarker),
        typeof(DES.Modules.DocumentIntelligence.DocumentIntelligenceModule),
        typeof(DES.Modules.Chat.Contracts.v1.Commands.CreateChannelCommand),
        typeof(DES.Modules.Chat.ChatModule),
        typeof(DES.Modules.Notifications.Contracts.v1.Commands.MarkNotificationReadCommand),
        typeof(DES.Modules.Notifications.NotificationsModule)];
});

var moduleAssemblies = new Assembly[]
{
    typeof(IdentityModule).Assembly,
    typeof(MultitenancyModule).Assembly,
    typeof(AuditingModule).Assembly,
    typeof(DES.Modules.Files.FilesModule).Assembly,
    typeof(WebhooksModule).Assembly,
    typeof(BillingModule).Assembly,
    typeof(CatalogModule).Assembly,
    typeof(TicketsModule).Assembly,
    typeof(DES.Modules.Rules.RulesModule).Assembly,
    typeof(DES.Modules.Notifications.NotificationsModule).Assembly,
    typeof(DES.Modules.Ppap.PpapModule).Assembly,
    typeof(DES.Modules.DocumentIntelligence.DocumentIntelligenceModule).Assembly,
    typeof(DES.Modules.Chat.ChatModule).Assembly,
};

builder.AddHeroPlatform(o =>
{
    o.EnableCaching = true;
    o.EnableMailing = true;
    o.EnableJobs = true;
    o.EnableQuotas = true;
    o.EnableSse = true;
    o.EnableRealtime = true;
});

// The transactional outbox is framework infrastructure with exactly one owner
// (EventingDbContext), so the host registers it once for every module (issue #1349).
builder.Services.AddEventingCore(builder.Configuration);

builder.AddModules(moduleAssemblies);

// Self-heal deployments carrying retired per-module `{module}-outbox-dispatcher` Hangfire recurring jobs
// (the outbox is now dispatched by OutboxDispatcherHostedService). No-op once the storage is clean.
builder.Services.AddHostedService<DES.Starter.Api.OrphanedOutboxRecurringJobCleanupService>();

// Demo data is provisioned by the DbMigrator's `seed-demo` verb, not the API — the API never mutates data on startup.
// See src/Host/DES.Starter.DbMigrator/README.md.

var app = builder.Build();

app.UseHeroMultiTenantDatabases();
app.UseHeroPlatform(p =>
{
    p.MapModules = true;
    p.ServeStaticFiles = true;
    p.UseQuotas = true;
    p.MapSseEndpoints = true;
    p.MapRealtime = true;
});

app.MapGet("/", () => Results.Ok(new { message = "hello world!" }))
   .WithTags("PlayGround")
   .AllowAnonymous();
await app.RunAsync();