using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapWaiverRevocationConfiguration : IEntityTypeConfiguration<PpapWaiverRevocation>
{
    public void Configure(EntityTypeBuilder<PpapWaiverRevocation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapWaiverRevocations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Reason).IsRequired().HasMaxLength(PpapWaiverRevocation.ReasonMaxLength);
        builder.HasIndex(x => new { x.TenantId, x.WaiverId }).IsUnique();
        builder.HasOne<PpapFindingWaiver>()
            .WithMany()
            .HasForeignKey(x => x.WaiverId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
