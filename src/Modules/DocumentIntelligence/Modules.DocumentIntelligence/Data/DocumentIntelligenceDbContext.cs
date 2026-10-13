using DES.Framework.Persistence.Context;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.DocumentIntelligence.Domain;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DES.Modules.DocumentIntelligence.Data;

public sealed class DocumentIntelligenceDbContext : BaseDbContext
{
    public const string Schema = "document_intelligence";

    public DbSet<ExtractionAttempt> ExtractionAttempts => Set<ExtractionAttempt>();
    public DbSet<CanonicalDocument> CanonicalDocuments => Set<CanonicalDocument>();

    public DocumentIntelligenceDbContext(
        IMultiTenantContextAccessor<AppTenantInfo> multiTenantContextAccessor,
        DbContextOptions<DocumentIntelligenceDbContext> options,
        IOptions<DatabaseOptions> settings,
        IHostEnvironment environment) : base(multiTenantContextAccessor, options, settings, environment) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DocumentIntelligenceDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
