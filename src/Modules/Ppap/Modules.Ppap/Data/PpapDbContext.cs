using Finbuckle.MultiTenant.Abstractions;
using DES.Framework.Persistence.Context;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DES.Modules.Ppap.Data;

public sealed class PpapDbContext : BaseDbContext
{
    public const string Schema = "ppap";

    public DbSet<PpapPart> PpapParts => Set<PpapPart>();
    public DbSet<PpapSupplier> PpapSuppliers => Set<PpapSupplier>();
    public DbSet<PpapSubmission> PpapSubmissions => Set<PpapSubmission>();
    public DbSet<PpapSubmissionDocument> PpapSubmissionDocuments => Set<PpapSubmissionDocument>();
    public DbSet<PpapDocumentRevision> PpapDocumentRevisions => Set<PpapDocumentRevision>();
    public DbSet<PpapJob> PpapJobs => Set<PpapJob>();
    public DbSet<PpapReport> PpapReports => Set<PpapReport>();
    public DbSet<PpapFinding> PpapFindings => Set<PpapFinding>();
    public DbSet<PpapManualFinding> PpapManualFindings => Set<PpapManualFinding>();
    public DbSet<PpapFindingWaiver> PpapFindingWaivers => Set<PpapFindingWaiver>();
    public DbSet<PpapWaiverRevocation> PpapWaiverRevocations => Set<PpapWaiverRevocation>();
    public DbSet<PpapWorkingFinding> PpapWorkingFindings => Set<PpapWorkingFinding>();
    public DbSet<PpapWaiverRequest> PpapWaiverRequests => Set<PpapWaiverRequest>();
    public DbSet<PpapComplexValidationRequest> PpapComplexValidationRequests => Set<PpapComplexValidationRequest>();
    public DbSet<CreditAccount> CreditAccounts => Set<CreditAccount>();
    public DbSet<CreditTransaction> CreditTransactions => Set<CreditTransaction>();
    public DbSet<CreditTopupRequest> CreditTopupRequests => Set<CreditTopupRequest>();

    public PpapDbContext(
        IMultiTenantContextAccessor<AppTenantInfo> multiTenantContextAccessor,
        DbContextOptions<PpapDbContext> options,
        IOptions<DatabaseOptions> settings,
        IHostEnvironment environment) : base(multiTenantContextAccessor, options, settings, environment) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PpapDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
