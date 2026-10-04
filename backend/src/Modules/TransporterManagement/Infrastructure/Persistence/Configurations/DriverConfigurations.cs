using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Configurations;

internal sealed class TransporterDriverConfiguration : IEntityTypeConfiguration<TransporterDriver>
{
    public void Configure(EntityTypeBuilder<TransporterDriver> b)
    {
        b.ToTable("tm_transporter_drivers");
        b.Property(x => x.FullName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Mobile).HasMaxLength(30).IsRequired();
        b.Property(x => x.LicenceNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => new { x.TransporterId, x.LicenceNumber }).IsUnique();
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>Adds the driver reference to documents. Kept separate from the document configuration for clarity.</summary>
internal sealed class TransporterDocumentDriverConfiguration : IEntityTypeConfiguration<TransporterDocument>
{
    public void Configure(EntityTypeBuilder<TransporterDocument> b)
    {
        b.HasIndex(x => x.DriverId);
        b.HasOne<TransporterDriver>().WithMany().HasForeignKey(x => x.DriverId).OnDelete(DeleteBehavior.Restrict);
    }
}
