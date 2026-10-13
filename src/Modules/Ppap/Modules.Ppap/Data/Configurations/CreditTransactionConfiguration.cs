using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class CreditTransactionConfiguration : IEntityTypeConfiguration<CreditTransaction>
{
    public void Configure(EntityTypeBuilder<CreditTransaction> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("CreditTransactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.CreditAccountId).IsRequired();
        builder.Property(x => x.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.Amount).IsRequired();
        builder.Property(x => x.Reference).IsRequired().HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => new { x.CreditAccountId, x.Kind, x.Reference }).IsUnique();
        builder.Ignore(x => x.DomainEvents);
    }
}
