using Finbuckle.MultiTenant.Abstractions;
using DES.Framework.Persistence.Context;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Notifications.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DES.Modules.Notifications.Data;

public sealed class NotificationsDbContext : BaseDbContext
{
    public const string Schema = "notifications";

    public NotificationsDbContext(
        IMultiTenantContextAccessor<AppTenantInfo> multiTenantContextAccessor,
        DbContextOptions<NotificationsDbContext> options,
        IOptions<DatabaseOptions> settings,
        IHostEnvironment environment) : base(multiTenantContextAccessor, options, settings, environment) { }

    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationsDbContext).Assembly);
        // base.OnModelCreating runs LAST so BaseDbContext's auto-apply (ApplyTenantIsolationByDefault)
        // sees fully-configured entities, including child types reached via HasMany navigation.
        base.OnModelCreating(modelBuilder);
    }
}
