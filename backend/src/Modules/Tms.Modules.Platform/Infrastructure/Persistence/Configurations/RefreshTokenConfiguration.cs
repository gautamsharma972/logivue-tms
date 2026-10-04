using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Platform.Domain;

namespace Tms.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(t => t.CreatedByIp).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => t.UserId);
    }
}
