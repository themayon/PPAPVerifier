using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapPartConfiguration : IEntityTypeConfiguration<PpapPart>
{
    public void Configure(EntityTypeBuilder<PpapPart> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapParts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.OwnerUserId).IsRequired();
        builder.Property(x => x.PartName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.PartNumber).IsRequired().HasMaxLength(100);
        builder.Property(x => x.DrawingRevision).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Version).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.PartNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.IsActive });
        builder.HasIndex(x => new { x.RuleTemplateId, x.RuleTemplateVersionId });
        builder.Ignore(x => x.DomainEvents);
    }
}
