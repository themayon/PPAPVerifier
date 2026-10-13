using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapFindingWaiverConfiguration : IEntityTypeConfiguration<PpapFindingWaiver>
{
    public void Configure(EntityTypeBuilder<PpapFindingWaiver> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapFindingWaivers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.TargetType).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(PpapFindingWaiver.ReasonMaxLength);
        builder.HasIndex(x => new { x.TenantId, x.TargetType, x.TargetFindingId, x.IsActive })
            .IsUnique()
            .HasFilter("\"IsActive\" = TRUE");
        builder.HasIndex(x => new { x.TenantId, x.ReportId, x.TargetType });
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.ReportId });
    }
}
