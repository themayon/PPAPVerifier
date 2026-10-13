using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class CreditAccountConfiguration : IEntityTypeConfiguration<CreditAccount>
{
    public void Configure(EntityTypeBuilder<CreditAccount> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("CreditAccounts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.OwnerUserId).IsRequired();
        builder.Property(x => x.AvailableCredits).IsRequired();
        builder.Property(x => x.ReservedCredits).IsRequired();
        builder.Property(x => x.Version).IsRequired().IsConcurrencyToken();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId }).IsUnique();
        builder.HasMany(x => x.Transactions)
            .WithOne()
            .HasForeignKey(x => x.CreditAccountId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(x => x.DomainEvents);
    }
}
