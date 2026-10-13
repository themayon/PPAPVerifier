using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapJobConfiguration : IEntityTypeConfiguration<PpapJob>
{
    public void Configure(EntityTypeBuilder<PpapJob> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapJobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.SubmissionVersion).IsRequired();
        builder.Property(x => x.OwnerUserId).IsRequired();
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.CurrentStage).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ProgressPercent).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.ProcessingLeaseId);
        builder.Property(x => x.ProcessingLeaseExpiresAtUtc);
        builder.Property(x => x.ErrorCode).HasMaxLength(PpapJob.ErrorCodeMaxLength);
        builder.Property(x => x.ErrorMessage).HasMaxLength(PpapJob.ErrorMessageMaxLength);
        builder.Property(x => x.RuleEvaluationQueuedAtUtc);
        builder.Property(x => x.RuleEvaluationStartedAtUtc);
        builder.Property(x => x.RuleEvaluationCompletedAtUtc);
        builder.Property(x => x.RuleEvaluationRunId);
        builder.Property(x => x.ReportGenerationQueuedAtUtc);
        builder.Property(x => x.ReportGenerationStartedAtUtc);
        builder.Property(x => x.ReportGenerationCompletedAtUtc);
        builder.Property(x => x.ReportGenerationReportId);
        builder.HasIndex(x => new { x.SubmissionId, x.SubmissionVersion }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.Status, x.ProcessingLeaseExpiresAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
        builder.Ignore(x => x.DomainEvents);
    }
}
