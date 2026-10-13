using DES.Framework.Core.Domain;
using DES.Framework.Persistence;
using DES.Framework.Persistence.Context;
using DES.Framework.Shared.Multitenancy;
using DES.Framework.Shared.Persistence;
using DES.Modules.Rules.Contracts;
using DES.Modules.Rules.Domain;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DES.Modules.Rules.Persistence;

public sealed class RulesDbContext(IMultiTenantContextAccessor<AppTenantInfo> tenantAccessor, DbContextOptions<RulesDbContext> options, IOptions<DatabaseOptions> settings, IHostEnvironment environment) : BaseDbContext(tenantAccessor, options, settings, environment)
{
    public const string Schema = "rules";
    public DbSet<RuleDefinition> RuleDefinitions => Set<RuleDefinition>();
    public DbSet<RuleVersion> RuleVersions => Set<RuleVersion>();
    public DbSet<RuleTemplate> RuleTemplates => Set<RuleTemplate>();
    public DbSet<RuleTemplateVersion> RuleTemplateVersions => Set<RuleTemplateVersion>();
    public DbSet<TemplateDocumentRequirement> TemplateDocumentRequirements => Set<TemplateDocumentRequirement>();
    public DbSet<TemplateRuleAssignment> TemplateRuleAssignments => Set<TemplateRuleAssignment>();
    public DbSet<RuleEvaluationRun> RuleEvaluationRuns => Set<RuleEvaluationRun>();
    public DbSet<RuleEvaluationResultRecord> RuleEvaluationResults => Set<RuleEvaluationResultRecord>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder); modelBuilder.HasDefaultSchema(Schema); modelBuilder.ApplyConfigurationsFromAssembly(typeof(RulesDbContext).Assembly); base.OnModelCreating(modelBuilder);
    }
}

public sealed class RuleDefinitionConfiguration : IEntityTypeConfiguration<RuleDefinition>
{
    public void Configure(EntityTypeBuilder<RuleDefinition> builder)
    {
        builder.ToTable("RuleDefinitions"); builder.HasKey(x => x.Id); builder.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.StableCode).HasMaxLength(64).IsRequired(); builder.HasIndex(x => new { x.TenantId, x.StableCode }).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired(); builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(32); builder.Property(x => x.Lifecycle).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Version).IsConcurrencyToken(); builder.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.RuleDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class RuleVersionConfiguration : IEntityTypeConfiguration<RuleVersion>
{
    public void Configure(EntityTypeBuilder<RuleVersion> builder)
    {
        builder.ToTable("RuleVersions"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.RuleDefinitionId, x.VersionNumber }).IsUnique(); builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.AstJson).HasColumnType("jsonb").IsRequired(); builder.Property(x => x.OutcomeJson).HasColumnType("jsonb").IsRequired(); builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class RuleTemplateConfiguration : IEntityTypeConfiguration<RuleTemplate>
{
    public void Configure(EntityTypeBuilder<RuleTemplate> builder)
    {
        builder.ToTable("RuleTemplates"); builder.HasKey(x => x.Id); builder.Property(x => x.TenantId).HasMaxLength(64).IsRequired();
        builder.Property(x => x.StableCode).HasMaxLength(64).IsRequired(); builder.HasIndex(x => new { x.TenantId, x.StableCode }).IsUnique();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired(); builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Lifecycle).HasConversion<string>().HasMaxLength(16); builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasMany(x => x.Versions).WithOne().HasForeignKey(x => x.RuleTemplateId).OnDelete(DeleteBehavior.Cascade); builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class RuleTemplateVersionConfiguration : IEntityTypeConfiguration<RuleTemplateVersion>
{
    public void Configure(EntityTypeBuilder<RuleTemplateVersion> builder)
    {
        builder.ToTable("RuleTemplateVersions"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.RuleTemplateId, x.VersionNumber }).IsUnique(); builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasMany(x => x.DocumentRequirements).WithOne().HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.RuleAssignments).WithOne().HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.Cascade); builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class TemplateDocumentRequirementConfiguration : IEntityTypeConfiguration<TemplateDocumentRequirement>
{
    public void Configure(EntityTypeBuilder<TemplateDocumentRequirement> builder)
    {
        builder.ToTable("TemplateDocumentRequirements"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DocumentCode).HasMaxLength(32).IsRequired(); builder.Property(x => x.Disposition).HasConversion<string>().HasMaxLength(16).IsRequired(); builder.HasIndex(x => new { x.TemplateVersionId, x.DocumentCode }).IsUnique(); builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class TemplateRuleAssignmentConfiguration : IEntityTypeConfiguration<TemplateRuleAssignment>
{
    public void Configure(EntityTypeBuilder<TemplateRuleAssignment> builder)
    {
        builder.ToTable("TemplateRuleAssignments"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DocumentCode).HasMaxLength(32); builder.Property(x => x.ParametersJson).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.TemplateVersionId, x.RuleVersionId, x.DocumentCode }).IsUnique().AreNullsDistinct(false);
        builder.HasOne<RuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId).OnDelete(DeleteBehavior.Restrict); builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class RuleEvaluationRunConfiguration : IEntityTypeConfiguration<RuleEvaluationRun>
{
    public void Configure(EntityTypeBuilder<RuleEvaluationRun> builder)
    {
        builder.ToTable("RuleEvaluationRuns"); builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).HasMaxLength(64).IsRequired(); builder.Property(x => x.InputSchemaVersion).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(32); builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.CreatedAtUtc }); builder.HasIndex(x => new { x.TenantId, x.SubjectId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.SubjectId, x.TemplateVersionId }).IsUnique();
        builder.HasOne<RuleTemplateVersion>().WithMany().HasForeignKey(x => x.TemplateVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Results).WithOne().HasForeignKey(x => x.RuleEvaluationRunId).OnDelete(DeleteBehavior.Cascade); builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class RuleEvaluationResultRecordConfiguration : IEntityTypeConfiguration<RuleEvaluationResultRecord>
{
    public void Configure(EntityTypeBuilder<RuleEvaluationResultRecord> builder)
    {
        builder.ToTable("RuleEvaluationResults"); builder.HasKey(x => x.Id); builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.DocumentCode).HasMaxLength(32); builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Reason).HasMaxLength(1000); builder.Property(x => x.TraceJson).HasColumnType("jsonb").HasMaxLength(RuleEvaluationResultRecord.MaxTraceBytes).IsRequired();
        builder.HasIndex(x => new { x.RuleEvaluationRunId, x.RuleVersionId, x.DocumentCode }).IsUnique().AreNullsDistinct(false);
        builder.HasOne<RuleDefinition>().WithMany().HasForeignKey(x => x.RuleDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RuleVersion>().WithMany().HasForeignKey(x => x.RuleVersionId).OnDelete(DeleteBehavior.Restrict); builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class RulesDbInitializer(RulesDbContext dbContext, TimeProvider timeProvider, ILogger<RulesDbInitializer> logger) : IDbInitializer
{
    private static readonly Guid SystemUserId = Guid.Parse("91a14482-7f9c-4a2b-917c-7db88994033c");

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        if ((await dbContext.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Any()) { await dbContext.Database.MigrateAsync(cancellationToken).ConfigureAwait(false); logger.LogInformation("[Rules] applied migrations"); }
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        string tenantId = dbContext.TenantInfo?.Id ?? throw new InvalidOperationException("Tenant context is required to seed PPAP level templates.");
        string[] stableCodes = PpapLevelMatrix.Levels.Select(x => x.StableCode).ToArray();
        HashSet<string> existingCodes = (await dbContext.RuleTemplates
            .Where(x => stableCodes.Contains(x.StableCode))
            .Select(x => x.StableCode)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).ToHashSet(StringComparer.Ordinal);
        DateTime utcNow = timeProvider.GetUtcNow().UtcDateTime;

        foreach (PpapLevelDefinition definition in PpapLevelMatrix.Levels.Where(x => !existingCodes.Contains(x.StableCode)))
        {
            RuleTemplate template = RuleTemplate.Create(tenantId, definition.StableCode, definition.Name, definition.Description, SystemUserId, utcNow);
            RuleTemplateVersion version = template.Versions.Single();
            TemplateDocumentRequirementInput[] requirements = definition.Documents
                .Select((document, index) => new TemplateDocumentRequirementInput(document.Code, document.Disposition != TemplateDocumentDisposition.Retain, index + 1, document.Disposition))
                .ToArray();
            template.ConfigureDraft(version.Id, template.Version, definition.Name, definition.Description, requirements, [], utcNow);
            template.SubmitForReview(version.Id, template.Version, utcNow);
            template.Publish(version.Id, template.Version, true, SystemUserId, utcNow);
            dbContext.RuleTemplates.Add(template);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
