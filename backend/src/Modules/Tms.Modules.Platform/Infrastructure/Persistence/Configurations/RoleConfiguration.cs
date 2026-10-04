using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Platform.Domain;

namespace Tms.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.Audience).HasConversion<string>().HasMaxLength(16).HasDefaultValue(RoleAudience.Internal);
        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();

        builder.Property(r => r.Permissions)
            .HasColumnType("json")
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<string[]>(v, (JsonSerializerOptions?)null) ?? Array.Empty<string>(),
                new ValueComparer<IReadOnlyList<string>>(
                    (a, b) => a!.SequenceEqual(b!),
                    v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode(StringComparison.Ordinal))),
                    v => v.ToArray()));
    }
}
