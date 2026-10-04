using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Platform.Domain;

namespace Tms.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.Property(u => u.Email).HasMaxLength(254).IsRequired();
        builder.Property(u => u.FullName).HasMaxLength(200).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(u => u.Type).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();

        builder.HasMany(u => u.Roles)
            .WithMany()
            .UsingEntity<Dictionary<string, object>>(
                "user_roles",
                right => right.HasOne<Role>().WithMany().HasForeignKey("RoleId").OnDelete(DeleteBehavior.Cascade),
                left => left.HasOne<User>().WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("user_roles");
                    join.HasKey("UserId", "RoleId");
                });

        builder.Navigation(u => u.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
