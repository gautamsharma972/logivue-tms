using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Configurations;

internal sealed class TransporterConfiguration : IEntityTypeConfiguration<Transporter>
{
    public void Configure(EntityTypeBuilder<Transporter> b)
    {
        b.ToTable("tm_transporters");
        b.Property(x => x.TransporterCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.LegalName).HasMaxLength(200).IsRequired();
        b.Property(x => x.TradeName).HasMaxLength(200);
        b.Property(x => x.CompanyType).HasMaxLength(50);
        b.Property(x => x.Pan).HasMaxLength(10);
        b.Property(x => x.Gstin).HasMaxLength(15);
        b.Property(x => x.RegistrationNumber).HasMaxLength(60);
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.State).HasMaxLength(100);
        b.Property(x => x.Country).HasMaxLength(100);
        b.Property(x => x.PrimaryContactName).HasMaxLength(150);
        b.Property(x => x.PrimaryContactEmail).HasMaxLength(254);
        b.Property(x => x.PrimaryContactPhone).HasMaxLength(30);
        b.Property(x => x.Website).HasMaxLength(200);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.Property(x => x.UpdatedBy).HasMaxLength(100);

        b.HasIndex(x => x.TransporterCode).IsUnique();
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Gstin);

        b.HasOne<TransporterType>().WithMany().HasForeignKey(x => x.TransporterTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterContactConfiguration : IEntityTypeConfiguration<TransporterContact>
{
    public void Configure(EntityTypeBuilder<TransporterContact> b)
    {
        b.ToTable("tm_transporter_contacts");
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.Property(x => x.Designation).HasMaxLength(100);
        b.Property(x => x.Email).HasMaxLength(254);
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.ContactType).HasMaxLength(50);
        b.HasIndex(x => x.TransporterId);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterBranchConfiguration : IEntityTypeConfiguration<TransporterBranch>
{
    public void Configure(EntityTypeBuilder<TransporterBranch> b)
    {
        b.ToTable("tm_transporter_branches");
        b.Property(x => x.BranchCode).HasMaxLength(30).IsRequired();
        b.Property(x => x.BranchName).HasMaxLength(150).IsRequired();
        b.Property(x => x.Address).HasMaxLength(500);
        b.Property(x => x.City).HasMaxLength(100);
        b.Property(x => x.State).HasMaxLength(100);
        b.Property(x => x.ContactName).HasMaxLength(150);
        b.Property(x => x.ContactPhone).HasMaxLength(30);
        b.HasIndex(x => new { x.TransporterId, x.BranchCode }).IsUnique();
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterLaneConfiguration : IEntityTypeConfiguration<TransporterLane>
{
    public void Configure(EntityTypeBuilder<TransporterLane> b)
    {
        b.ToTable("tm_transporter_lanes");
        b.Property(x => x.ServiceType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => new { x.OriginLocationReference, x.DestinationLocationReference, x.ServiceType });
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterCapabilityConfiguration : IEntityTypeConfiguration<TransporterCapability>
{
    public void Configure(EntityTypeBuilder<TransporterCapability> b)
    {
        b.ToTable("tm_transporter_capabilities");
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.TransporterId, x.CapabilityTypeId });
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CapabilityType>().WithMany().HasForeignKey(x => x.CapabilityTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterDocumentConfiguration : IEntityTypeConfiguration<TransporterDocument>
{
    public void Configure(EntityTypeBuilder<TransporterDocument> b)
    {
        b.ToTable("tm_transporter_documents");
        b.Property(x => x.DocumentNumber).HasMaxLength(60);
        b.Property(x => x.FileReference).HasMaxLength(500);
        b.Property(x => x.OriginalFileName).HasMaxLength(255);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.VerifiedBy).HasMaxLength(100);
        b.Property(x => x.Remarks).HasMaxLength(1000);
        b.Property(x => x.VerificationStatus).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => x.DocumentTypeId);
        b.HasIndex(x => x.ExpiryDate);
        b.HasIndex(x => x.VerificationStatus);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DocumentType>().WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterVehicleConfiguration : IEntityTypeConfiguration<TransporterVehicle>
{
    public void Configure(EntityTypeBuilder<TransporterVehicle> b)
    {
        b.ToTable("tm_transporter_vehicles");
        b.Property(x => x.RegistrationNumber).HasMaxLength(20).IsRequired();
        b.Property(x => x.PayloadCapacityKg).HasPrecision(10, 2);
        b.Property(x => x.UsableVolumeM3).HasPrecision(10, 2);
        b.Property(x => x.LengthM).HasPrecision(6, 2);
        b.Property(x => x.WidthM).HasPrecision(6, 2);
        b.Property(x => x.HeightM).HasPrecision(6, 2);
        b.Property(x => x.OwnershipType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.AvailabilityStatus).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.TransporterId, x.RegistrationNumber }).IsUnique();
        b.HasIndex(x => x.VehicleTypeReference);
        b.HasIndex(x => x.AvailabilityStatus);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterUserConfiguration : IEntityTypeConfiguration<TransporterUser>
{
    public void Configure(EntityTypeBuilder<TransporterUser> b)
    {
        b.ToTable("tm_transporter_users");
        b.Property(x => x.Username).HasMaxLength(100).IsRequired();
        b.Property(x => x.Email).HasMaxLength(254).IsRequired();
        b.Property(x => x.Phone).HasMaxLength(30);
        b.Property(x => x.Role).HasMaxLength(60).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.Username).IsUnique();
        b.HasIndex(x => x.TransporterId);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterRateConfiguration : IEntityTypeConfiguration<TransporterRate>
{
    public void Configure(EntityTypeBuilder<TransporterRate> b)
    {
        b.ToTable("tm_transporter_rates");
        b.Property(x => x.ServiceType).HasMaxLength(50).IsRequired();
        b.Property(x => x.RateType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RateValue).HasPrecision(12, 2);
        b.Property(x => x.MinimumCharge).HasPrecision(12, 2);
        b.Property(x => x.FuelSurcharge).HasPrecision(12, 2);
        b.Property(x => x.TollAmount).HasPrecision(12, 2);
        b.Property(x => x.OtherCharges).HasPrecision(12, 2);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => new { x.OriginLocationReference, x.DestinationLocationReference, x.ServiceType });
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}
