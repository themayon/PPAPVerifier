using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapFindingConfiguration : IEntityTypeConfiguration<PpapFinding>
{
    public void Configure(EntityTypeBuilder<PpapFinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapFindings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.StableRuleCode).IsRequired().HasMaxLength(PpapFinding.StableRuleCodeMaxLength);
        builder.Property(x => x.RuleName).IsRequired().HasMaxLength(PpapFinding.RuleNameMaxLength);
        builder.Property(x => x.DocumentCode).HasMaxLength(PpapFinding.DocumentCodeMaxLength);
        builder.Property(x => x.Outcome).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Message).HasMaxLength(PpapFinding.MessageMaxLength);
        builder.Property(x => x.Reason).HasMaxLength(PpapFinding.ReasonMaxLength);
        builder.Property(x => x.TraceJson).IsRequired().HasColumnType("jsonb").HasMaxLength(PpapFinding.MaxTraceBytes);
        builder.HasIndex(x => new { x.TenantId, x.RuleEvaluationResultId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.JobId, x.Outcome });
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.SubmissionId });
        builder.Ignore(x => x.DomainEvents);
    }
}
