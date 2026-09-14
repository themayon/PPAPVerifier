using DES.Framework.Shared.Multitenancy;
using DES.Modules.Multitenancy.Provisioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DES.Modules.Multitenancy.Data.Configurations;

public class TenantProvisioningConfiguration : IEntityTypeConfiguration<TenantProvisioning>
{
    public void Configure(EntityTypeBuilder<TenantProvisioning> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("TenantProvisionings", MultitenancyConstants.Schema);

        builder.HasMany(p => p.Steps)
            .WithOne(s => s.Provisioning!)
            .HasForeignKey(s => s.ProvisioningId);
    }
}