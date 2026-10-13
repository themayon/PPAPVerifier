using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapManualFindingConfiguration : IEntityTypeConfiguration<PpapManualFinding>
{
    public void Configure(EntityTypeBuilder<PpapManualFinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapManualFindings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Message).IsRequired().HasMaxLength(PpapManualFinding.MessageMaxLength);
        builder.Property(x => x.EvidenceNote).HasMaxLength(PpapManualFinding.EvidenceNoteMaxLength);
        builder.Property(x => x.DocumentCode).HasMaxLength(PpapSubmissionDocument.DocumentCodeMaxLength);
        builder.HasIndex(x => new { x.TenantId, x.ReportId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.ReportId });
        builder.HasIndex(x => new { x.TenantId, x.SubmissionDocumentId });
    }
}
