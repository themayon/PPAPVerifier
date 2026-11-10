using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapWorkingFindingConfiguration : IEntityTypeConfiguration<PpapWorkingFinding>
{
    public void Configure(EntityTypeBuilder<PpapWorkingFinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapWorkingFindings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.StableRuleCode).IsRequired().HasMaxLength(PpapWorkingFinding.StableRuleCodeMaxLength);
        builder.Property(x => x.RuleName).IsRequired().HasMaxLength(PpapWorkingFinding.RuleNameMaxLength);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Message).IsRequired().HasMaxLength(PpapWorkingFinding.MessageMaxLength);
        builder.Property(x => x.InputFingerprint).IsRequired().HasMaxLength(PpapWorkingFinding.InputFingerprintMaxLength);
        builder.Property(x => x.EvidenceJson).IsRequired().HasColumnType("jsonb");
        builder.HasIndex(x => new { x.TenantId, x.SubmissionId, x.IsCurrent });
        builder.HasIndex(x => new { x.TenantId, x.RuleVersionId, x.InputFingerprint }).IsUnique().HasFilter("\"IsCurrent\" = TRUE");
        builder.HasIndex(x => x.DocumentRevisionId);
    }
}
