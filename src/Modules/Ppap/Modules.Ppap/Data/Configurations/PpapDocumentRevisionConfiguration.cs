using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapDocumentRevisionConfiguration : IEntityTypeConfiguration<PpapDocumentRevision>
{
    public void Configure(EntityTypeBuilder<PpapDocumentRevision> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapDocumentRevisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.SourceFileName).IsRequired().HasMaxLength(PpapDocumentRevision.FileNameMaxLength);
        builder.Property(x => x.SourceContentType).IsRequired().HasMaxLength(PpapDocumentRevision.ContentTypeMaxLength);
        builder.Property(x => x.UploadStatus).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ProcessingStatus).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.Provider).HasMaxLength(PpapDocumentRevision.ProviderMaxLength);
        builder.Property(x => x.ExtractorVersion).HasMaxLength(PpapDocumentRevision.ExtractorVersionMaxLength);
        builder.Property(x => x.FailureCode).HasMaxLength(PpapDocumentRevision.FailureCodeMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.TenantId, x.SubmissionDocumentId, x.RevisionNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.SubmissionDocumentId }).IsUnique().HasFilter("\"IsCurrent\" = TRUE");
        builder.HasIndex(x => x.SourceFileAssetId).IsUnique();
        builder.HasIndex(x => x.ExtractionRequestEventId).IsUnique();
        builder.HasIndex(x => x.ExtractionAttemptId).IsUnique();
        builder.HasIndex(x => x.ProcessingStatus);
    }
}
