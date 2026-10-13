using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapReportConfiguration : IEntityTypeConfiguration<PpapReport>
{
    public void Configure(EntityTypeBuilder<PpapReport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapReports");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.JobId).IsRequired();
        builder.Property(x => x.SubmissionVersion).IsRequired();
        builder.Property(x => x.OwnerUserId).IsRequired();
        builder.Property(x => x.GeneratedAtUtc).IsRequired();
        builder.Property(x => x.ReproducibilityManifestJson)
            .HasColumnType("jsonb")
            .HasMaxLength(PpapReport.ReproducibilityManifestMaxBytes);
        builder.HasIndex(x => x.JobId).IsUnique();
        builder.HasIndex(x => new { x.SubmissionId, x.SubmissionVersion }).IsUnique();
        builder.HasIndex(x => new { x.OwnerUserId, x.GeneratedAtUtc });
        builder.Ignore(x => x.DomainEvents);
    }
}
