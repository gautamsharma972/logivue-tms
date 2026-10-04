using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Platform.Domain;

namespace Tms.Modules.Platform.Infrastructure.Persistence.Configurations;

internal sealed class SequenceRowConfiguration : IEntityTypeConfiguration<SequenceRow>
{
    public void Configure(EntityTypeBuilder<SequenceRow> builder)
    {
        builder.ToTable("sequences");
        builder.HasKey(s => new { s.TenantId, s.Name });
        builder.Property(s => s.Name).HasMaxLength(64);
    }
}
