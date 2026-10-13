using DES.Modules.Ppap.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Ppap.Data.Configurations;

public sealed class CreditTopupRequestConfiguration : IEntityTypeConfiguration<CreditTopupRequest>
{
    public void Configure(EntityTypeBuilder<CreditTopupRequest> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("CreditTopupRequests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(64);
        builder.Property(x => x.OwnerUserId).IsRequired();
        builder.Property(x => x.CreditsRequested).IsRequired();
        builder.Property(x => x.PaymentReference).HasMaxLength(CreditTopupRequest.MaxPaymentReferenceLength);
        builder.Property(x => x.Note).HasMaxLength(CreditTopupRequest.MaxNoteLength);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.RequestedAt).IsRequired();
        builder.Property(x => x.DecisionNote).HasMaxLength(CreditTopupRequest.MaxDecisionNoteLength);
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.RequestedAt });
        builder.HasIndex(x => new { x.TenantId, x.Status, x.RequestedAt });
        builder.Ignore(x => x.DomainEvents);
    }
}
