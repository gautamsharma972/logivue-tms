using LogiVue.Tms.TransporterManagement.Domain.Claims;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Configurations;

internal sealed class TransporterClaimConfiguration : IEntityTypeConfiguration<TransporterClaim>
{
    public void Configure(EntityTypeBuilder<TransporterClaim> b)
    {
        b.ToTable("tm_claims");
        b.Property(x => x.LoadReference).HasMaxLength(60);
        b.Property(x => x.ClaimType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ClaimValue).HasPrecision(14, 2);
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.ResolvedBy).HasMaxLength(100);
        b.HasIndex(x => new { x.TransporterId, x.ClaimDate });
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LoadCostConfiguration : IEntityTypeConfiguration<LoadCost>
{
    public void Configure(EntityTypeBuilder<LoadCost> b)
    {
        b.ToTable("tm_load_costs");
        b.Property(x => x.LoadReference).HasMaxLength(60).IsRequired();
        b.Property(x => x.AgreedAmount).HasPrecision(14, 2);
        b.Property(x => x.InvoicedAmount).HasPrecision(14, 2);
        b.Property(x => x.CreatedBy).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.TransporterId, x.ServiceDate });
        b.HasIndex(x => new { x.TransporterId, x.LoadReference }).IsUnique();
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CapacityDayConfiguration : IEntityTypeConfiguration<CapacityDay>
{
    public void Configure(EntityTypeBuilder<CapacityDay> b)
    {
        b.ToTable("tm_capacity_days");
        b.Property(x => x.UpdatedBy).HasMaxLength(100).IsRequired();
        b.HasIndex(x => new { x.TransporterId, x.Date }).IsUnique();
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}
