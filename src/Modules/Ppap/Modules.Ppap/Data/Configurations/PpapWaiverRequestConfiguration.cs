using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapWaiverRequestConfiguration : IEntityTypeConfiguration<PpapWaiverRequest>
{
    public void Configure(EntityTypeBuilder<PpapWaiverRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapWaiverRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.FindingFingerprint).IsRequired().HasMaxLength(PpapWorkingFinding.InputFingerprintMaxLength);
        builder.Property(x => x.RequestReason).IsRequired().HasMaxLength(PpapWaiverRequest.ReasonMaxLength);
        builder.Property(x => x.ReviewReason).HasMaxLength(PpapWaiverRequest.ReasonMaxLength);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.TenantId, x.SubmissionId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.WorkingFindingId }).IsUnique().HasFilter("\"Status\" = 'Pending'");
    }
}
