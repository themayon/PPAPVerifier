using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapSubmissionConfiguration : IEntityTypeConfiguration<PpapSubmission>
{
    public void Configure(EntityTypeBuilder<PpapSubmission> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapSubmissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.OwnerUserId).IsRequired();
        builder.Property(x => x.PartId);
        builder.Property(x => x.PartName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PartNumber).IsRequired().HasMaxLength(100);
        builder.Property(x => x.DrawingRevision).IsRequired().HasMaxLength(50);
        builder.Property(x => x.PpapLevel).IsRequired();
        builder.Property(x => x.RuleTemplateId);
        builder.Property(x => x.RuleTemplateVersionId);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Version).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => x.OwnerUserId);
        builder.HasIndex(x => x.PartId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => new { x.TenantId, x.PartNumber });
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.Status });
        builder.HasIndex(x => new { x.RuleTemplateId, x.RuleTemplateVersionId });
        builder.HasMany(x => x.Documents)
            .WithOne()
            .HasForeignKey(x => x.SubmissionId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Ignore(x => x.DomainEvents);
    }
}
