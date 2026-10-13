using DES.Modules.DocumentIntelligence.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.DocumentIntelligence.Data.Configurations;

public sealed class ExtractionAttemptConfiguration : IEntityTypeConfiguration<ExtractionAttempt>
{
    public void Configure(EntityTypeBuilder<ExtractionAttempt> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("ExtractionAttempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(ExtractionAttempt.TenantIdMaxLength);
        builder.Property(x => x.OwnerUserId).IsRequired();
        builder.Property(x => x.SubmissionDocumentId).IsRequired();
        builder.Property(x => x.SourceFileAssetId).IsRequired();
        builder.Property(x => x.DocumentCode).IsRequired().HasMaxLength(ExtractionAttempt.DocumentCodeMaxLength);
        builder.Property(x => x.SourceSha256).IsRequired().HasMaxLength(64).IsFixedLength();
        builder.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(ExtractionAttempt.IdempotencyKeyMaxLength);
        builder.Property(x => x.Provider).IsRequired().HasMaxLength(ExtractionAttempt.ProviderMaxLength);
        builder.Property(x => x.Model).IsRequired().HasMaxLength(ExtractionAttempt.ModelMaxLength);
        builder.Property(x => x.ExtractorVersion).IsRequired().HasMaxLength(ExtractionAttempt.ExtractorVersionMaxLength);
        builder.Property(x => x.Status).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(x => x.ProviderRequestId).HasMaxLength(ExtractionAttempt.ProviderRequestIdMaxLength);
        builder.Property(x => x.ErrorCode).HasMaxLength(ExtractionAttempt.ErrorCodeMaxLength);
        builder.Property(x => x.ErrorMessage).HasMaxLength(ExtractionAttempt.ErrorMessageMaxLength);
        builder.Property(x => x.ClaimedBy).HasMaxLength(ExtractionAttempt.ClaimedByMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique()
            .HasFilter("\"Status\" <> 'DeadLettered'");
        builder.HasIndex(x => new { x.TenantId, x.SubmissionDocumentId, x.SourceSha256, x.Provider, x.Model, x.ExtractorVersion })
            .IsUnique()
            .HasFilter("\"Status\" <> 'DeadLettered'");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.OwnerUserId, x.Status, x.UpdatedAtUtc });
        builder.Ignore(x => x.DomainEvents);
    }
}
