using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Transporters.Domain;

namespace Tms.Modules.Transporters.Infrastructure.Persistence.Configurations;

internal sealed class TransporterConfiguration : IEntityTypeConfiguration<Transporter>
{
    public void Configure(EntityTypeBuilder<Transporter> builder)
    {
        builder.ToTable("transporters");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Code).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.TradeName).HasMaxLength(200);
        builder.Property(t => t.Pan).HasMaxLength(10).IsRequired();
        builder.Property(t => t.Gstin).HasMaxLength(15);
        builder.Property(t => t.ContactPerson).HasMaxLength(150).IsRequired();
        builder.Property(t => t.Phone).HasMaxLength(15).IsRequired();
        builder.Property(t => t.Email).HasMaxLength(254).IsRequired();
        builder.Property(t => t.AddressLine1).HasMaxLength(200).IsRequired();
        builder.Property(t => t.AddressLine2).HasMaxLength(200);
        builder.Property(t => t.City).HasMaxLength(100).IsRequired();
        builder.Property(t => t.State).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Pincode).HasMaxLength(6).IsRequired();
        builder.Property(t => t.ServiceModes).HasConversion<int>();
        builder.Property(t => t.BankAccountHolder).HasMaxLength(200);
        builder.Property(t => t.BankIfsc).HasMaxLength(11);
        builder.Property(t => t.BankName).HasMaxLength(100);
        builder.Property(t => t.SuspensionReason).HasMaxLength(500);
        builder.Ignore(t => t.HasBankDetails);

        // The database is the last line of defence against duplicate vendors (a classic fraud and reconciliation problem).
        builder.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.Pan }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.Gstin }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.Status });
    }
}

internal sealed class VehicleTypeConfiguration : IEntityTypeConfiguration<VehicleType>
{
    public void Configure(EntityTypeBuilder<VehicleType> builder)
    {
        builder.ToTable("vehicle_types");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Code).HasMaxLength(32).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.VolumeCbm).HasPrecision(8, 2);
        builder.Property(t => t.LengthM).HasPrecision(5, 2);
        builder.Property(t => t.WidthM).HasPrecision(5, 2);
        builder.Property(t => t.HeightM).HasPrecision(5, 2);
        builder.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
    }
}

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> builder)
    {
        builder.ToTable("vehicles");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).ValueGeneratedNever();
        builder.Property(v => v.RegistrationNumber).HasMaxLength(12).IsRequired();
        builder.Property(v => v.Ownership).HasConversion<string>().HasMaxLength(16);
        builder.Property(v => v.Make).HasMaxLength(100);
        builder.Property(v => v.Availability).HasConversion<string>().HasMaxLength(16).HasDefaultValue(Tms.SharedKernel.Contracts.FleetAvailability.Available);
        builder.Property(v => v.AvailabilityNote).HasMaxLength(200);
        builder.HasIndex(v => new { v.TenantId, v.RegistrationNumber }).IsUnique();
        builder.HasIndex(v => new { v.TenantId, v.TransporterId });
    }
}

internal sealed class DriverConfiguration : IEntityTypeConfiguration<Driver>
{
    public void Configure(EntityTypeBuilder<Driver> builder)
    {
        builder.ToTable("drivers");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.FullName).HasMaxLength(150).IsRequired();
        builder.Property(d => d.Phone).HasMaxLength(15).IsRequired();
        builder.Property(d => d.LicenseNumber).HasMaxLength(20);
        builder.HasIndex(d => new { d.TenantId, d.LicenseNumber }).IsUnique();
        builder.HasIndex(d => new { d.TenantId, d.TransporterId });
    }
}

internal sealed class ComplianceDocumentConfiguration : IEntityTypeConfiguration<ComplianceDocument>
{
    public void Configure(EntityTypeBuilder<ComplianceDocument> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.OwnerKind).HasConversion<string>().HasMaxLength(16);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.Number).HasMaxLength(64);
        builder.Property(d => d.FileKey).HasMaxLength(300).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        builder.Ignore(d => d.IsCurrent);
        builder.HasIndex(d => new { d.TenantId, d.OwnerKind, d.OwnerId, d.Kind });
        builder.HasIndex(d => new { d.TenantId, d.TransporterId });
        builder.HasIndex(d => new { d.TenantId, d.ExpiresOn }); // the expiry report
    }
}

internal sealed class SequenceCounterConfiguration : IEntityTypeConfiguration<SequenceCounter>
{
    public void Configure(EntityTypeBuilder<SequenceCounter> builder)
    {
        builder.ToTable("sequences");
        builder.HasKey(s => new { s.TenantId, s.Name });
        builder.Property(s => s.Name).HasMaxLength(64);
    }
}
