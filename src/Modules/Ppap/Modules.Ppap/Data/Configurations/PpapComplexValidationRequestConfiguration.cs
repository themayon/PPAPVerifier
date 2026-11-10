using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapComplexValidationRequestConfiguration : IEntityTypeConfiguration<PpapComplexValidationRequest>
{
    public void Configure(EntityTypeBuilder<PpapComplexValidationRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapComplexValidationRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.ExecutorKey).IsRequired().HasMaxLength(PpapComplexValidationRequest.ExecutorKeyMaxLength);
        builder.Property(x => x.InputFingerprint).IsRequired().HasMaxLength(PpapComplexValidationRequest.InputFingerprintMaxLength);
        builder.HasIndex(x => x.RequestEventId).IsUnique();
        builder.HasIndex(x => x.ValidationRunId).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.SubmissionId, x.RuleVersionId, x.InputFingerprint }).IsUnique();
    }
}
