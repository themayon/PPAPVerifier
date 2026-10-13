using DES.Modules.DocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.DocumentIntelligence.Data.Configurations;

public sealed class CanonicalDocumentConfiguration : IEntityTypeConfiguration<CanonicalDocument>
{
    public void Configure(EntityTypeBuilder<CanonicalDocument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("CanonicalDocuments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(CanonicalDocument.TenantIdMaxLength);
        builder.Property(x => x.ExtractionAttemptId).IsRequired();
        builder.Property(x => x.OwnerUserId).IsRequired();
        builder.Property(x => x.SchemaVersion).IsRequired().HasMaxLength(CanonicalDocument.SchemaVersionMaxLength);
        builder.Property(x => x.CanonicalJson).IsRequired().HasColumnType("jsonb");
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => x.ExtractionAttemptId).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.CreatedAtUtc });
        builder.Ignore(x => x.DomainEvents);
    }
}
