using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Platform.Domain;

namespace Tms.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Code).HasMaxLength(32).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(t => t.Code).IsUnique();
    }
}
