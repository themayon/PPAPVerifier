using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class PpapSupplierConfiguration : IEntityTypeConfiguration<PpapSupplier>
{
    public void Configure(EntityTypeBuilder<PpapSupplier> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapSuppliers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.Code).IsRequired().HasMaxLength(50);
        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ContactName).HasMaxLength(200);
        builder.Property(x => x.Email).HasMaxLength(256);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.Address).HasMaxLength(500);
        builder.Property(x => x.TaxId).HasMaxLength(50);
        builder.Property(x => x.DunsNumber).HasMaxLength(9);
        builder.Property(x => x.City).HasMaxLength(100);
        builder.Property(x => x.Country).HasMaxLength(100);
        builder.Property(x => x.Region).HasMaxLength(32);
        builder.Property(x => x.TierLevel).HasMaxLength(16);
        builder.Property(x => x.QualityGrade).HasMaxLength(4);
        builder.Property(x => x.SiteName).HasMaxLength(200);
        builder.Property(x => x.SiteAddress).HasMaxLength(500);
        builder.Property(x => x.ContactJobTitle).HasMaxLength(200);
        builder.Property(x => x.Version).IsRequired().IsConcurrencyToken();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.IsActive });
        builder.HasIndex(x => new { x.TenantId, x.Region });
        builder.HasMany(x => x.Users).WithOne().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Certificates).WithOne().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class PpapSupplierCertificateConfiguration : IEntityTypeConfiguration<PpapSupplierCertificate>
{
    public void Configure(EntityTypeBuilder<PpapSupplierCertificate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapSupplierCertificates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.FileName).IsRequired().HasMaxLength(260);
        builder.Property(x => x.CertificateType).IsRequired().HasMaxLength(32);
        builder.HasIndex(x => new { x.SupplierId, x.FileAssetId }).IsUnique();
        builder.HasIndex(x => x.FileAssetId);
        builder.Ignore(x => x.DomainEvents);
    }
}

public sealed class PpapSupplierUserConfiguration : IEntityTypeConfiguration<PpapSupplierUser>
{
    public void Configure(EntityTypeBuilder<PpapSupplierUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("PpapSupplierUsers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.HasIndex(x => new { x.SupplierId, x.UserId }).IsUnique();
        builder.HasIndex(x => x.UserId);
        builder.Ignore(x => x.DomainEvents);
    }
}
