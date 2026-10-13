using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapSubmissionDocumentConfiguration : IEntityTypeConfiguration<PpapSubmissionDocument>
{
    public void Configure(EntityTypeBuilder<PpapSubmissionDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapSubmissionDocuments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SubmissionId).IsRequired();
        builder.Property(x => x.DocumentCode).IsRequired().HasMaxLength(PpapSubmissionDocument.DocumentCodeMaxLength);
        builder.Property(x => x.IsRequired).IsRequired();
        builder.Property(x => x.Disposition).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.NotApplicableReason).HasMaxLength(PpapSubmissionDocument.NotApplicableReasonMaxLength);
        builder.Property(x => x.ExtractionRequestEventId);
        builder.Property(x => x.ExtractionRequestPublishedAtUtc);
        builder.Property(x => x.ExtractionAttemptId);
        builder.Property(x => x.ExtractionTerminalStatus).HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.CanonicalDocumentId);
        builder.Property(x => x.RawResponseFileAssetId);
        builder.Property(x => x.GeneratedExcelFileAssetId);
        builder.Property(x => x.ExtractionFailureCode).HasMaxLength(PpapSubmissionDocument.ExtractionFailureCodeMaxLength);
        builder.Property(x => x.ExtractionCompletedAtUtc);
        builder.Property(x => x.ExtractionFailedAtUtc);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.SubmissionId, x.DocumentCode }).IsUnique();
        builder.HasIndex(x => x.FileAssetId);
        builder.HasIndex(x => x.ExtractionRequestEventId).IsUnique();
        builder.HasIndex(x => x.ExtractionAttemptId).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => new { x.TenantId, x.SubmissionId });
    }
}
