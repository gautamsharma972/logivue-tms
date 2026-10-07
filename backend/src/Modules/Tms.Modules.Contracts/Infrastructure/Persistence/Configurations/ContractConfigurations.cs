using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Persistence;

namespace Tms.Modules.Contracts.Infrastructure.Persistence.Configurations;

internal sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("contracts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Number).HasMaxLength(20).IsRequired();
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Title).HasMaxLength(200).IsRequired();
        builder.Property(c => c.EstimatedAnnualSpend).HasPrecision(18, 2);
        builder.Property(c => c.TerminationReason).HasMaxLength(500);
        builder.Property(c => c.Terms).HasJsonValue();
        builder.Property(c => c.Fuel).HasJsonValue();
        builder.Ignore(c => c.Reference);
        builder.Ignore(c => c.WasApproved);
        builder.Ignore(c => c.EffectiveServices);
        builder.Ignore(c => c.IsSuspended);
        builder.Property(c => c.Currency).HasMaxLength(3).IsRequired().HasDefaultValue("INR");
        builder.Property(c => c.BusinessUnit).HasMaxLength(100);
        builder.Property(c => c.PrimaryContact).HasMaxLength(200);
        builder.Property(c => c.RenewalNoticeDays).HasDefaultValue(60);
        builder.Property(c => c.CalculationVersion).HasMaxLength(10).IsRequired().HasDefaultValue("1.0");
        builder.Property(c => c.RevisionKind).HasConversion<string>().HasMaxLength(12).HasDefaultValue(RevisionKind.Original);
        builder.Property(c => c.Services).HasJsonValue().HasDefaultValueSql("(JSON_ARRAY())");
        builder.Property(c => c.Suspensions).HasJsonValue().HasDefaultValueSql("(JSON_ARRAY())");

        builder.HasMany(c => c.RateCards).WithOne().HasForeignKey(r => r.ContractId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.RateCards).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(c => c.DphRules).WithOne().HasForeignKey(r => r.ContractId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.DphRules).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(c => c.Accessorials).WithOne().HasForeignKey(r => r.ContractId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Accessorials).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(c => c.Capacities).WithOne().HasForeignKey(r => r.ContractId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Capacities).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(c => c.Slas).WithOne().HasForeignKey(r => r.ContractId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Slas).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.TenantId, c.Number, c.Revision }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.TransporterId, c.Status });
        builder.HasIndex(c => new { c.TenantId, c.Status, c.EffectiveTo }); // expiry watchlist and the nightly job
    }
}

internal sealed class RateCardConfiguration : IEntityTypeConfiguration<RateCard>
{
    public void Configure(EntityTypeBuilder<RateCard> builder)
    {
        builder.ToTable("rate_cards");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.OriginKind).HasConversion<string>().HasMaxLength(8);
        builder.Property(r => r.DestinationKind).HasConversion<string>().HasMaxLength(8);
        builder.Property(r => r.OriginState).HasMaxLength(100);
        builder.Property(r => r.OriginCity).HasMaxLength(100);
        builder.Property(r => r.OriginZone).HasMaxLength(32);
        builder.Property(r => r.DestinationState).HasMaxLength(100);
        builder.Property(r => r.DestinationCity).HasMaxLength(100);
        builder.Property(r => r.DestinationZone).HasMaxLength(32);
        builder.Property(r => r.MinDistanceKm).HasPrecision(9, 2);
        builder.Property(r => r.MaxDistanceKm).HasPrecision(9, 2);
        builder.Property(r => r.Pricing).HasJsonValue();
        builder.Ignore(r => r.Origin);
        builder.Ignore(r => r.Destination);
        builder.Ignore(r => r.HasDistanceBand);
        builder.Ignore(r => r.HasWeightBand);
        builder.Ignore(r => r.HasVolumeBand);
        builder.Ignore(r => r.Extras);
        builder.Property(r => r.Code).HasMaxLength(40).IsRequired();
        builder.Property(r => r.Version).HasDefaultValue(1);
        builder.Property(r => r.Priority).HasDefaultValue(RateExtras.DefaultPriority);
        builder.Property(r => r.MinimumCharge).HasPrecision(14, 2);
        builder.Property(r => r.MaximumCharge).HasPrecision(14, 2);
        builder.Property(r => r.MinWeightKg).HasPrecision(12, 2);
        builder.Property(r => r.MaxWeightKg).HasPrecision(12, 2);
        builder.Property(r => r.MinVolumeCbm).HasPrecision(10, 2);
        builder.Property(r => r.MaxVolumeCbm).HasPrecision(10, 2);
        builder.Property(r => r.RequiredCapabilities).HasJsonValue().HasDefaultValueSql("(JSON_ARRAY())");
        builder.Property(r => r.DphRuleCode).HasMaxLength(40);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.HasIndex(r => new { r.ContractId, r.Code }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.OriginCity, r.DestinationCity });
        builder.HasIndex(r => new { r.TenantId, r.VehicleTypeId });

        builder.HasIndex(r => r.ContractId);
        // Narrows rate lookups to plausible lanes before the finer in-memory matching.
        builder.HasIndex(r => new { r.TenantId, r.OriginState, r.DestinationState });
    }
}

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> builder)
    {
        builder.ToTable("zones");
        builder.HasKey(z => z.Id);
        builder.Property(z => z.Id).ValueGeneratedNever();
        builder.Property(z => z.Code).HasMaxLength(32).IsRequired();
        builder.Property(z => z.Name).HasMaxLength(100).IsRequired();
        builder.Property(z => z.Members).HasJsonValue();
        builder.HasIndex(z => new { z.TenantId, z.Code }).IsUnique();
    }
}

internal sealed class DieselPriceConfiguration : IEntityTypeConfiguration<DieselPrice>
{
    public void Configure(EntityTypeBuilder<DieselPrice> builder)
    {
        builder.ToTable("diesel_prices");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Region).HasMaxLength(64).IsRequired();
        builder.Property(d => d.PricePerLitre).HasPrecision(8, 2);
        builder.Property(d => d.Source).HasMaxLength(100);
        builder.HasIndex(d => new { d.TenantId, d.Region, d.EffectiveFrom }).IsUnique();
    }
}

internal sealed class ContractDocumentConfiguration : IEntityTypeConfiguration<ContractDocument>
{
    public void Configure(EntityTypeBuilder<ContractDocument> builder)
    {
        builder.ToTable("documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();
        builder.Property(d => d.FileKey).HasMaxLength(300).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Number).HasMaxLength(60);
        builder.Property(d => d.DocumentVersion).HasDefaultValue(1);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(10).HasDefaultValue(DocumentStatus.Pending);
        builder.HasIndex(d => new { d.TenantId, d.ContractId });
        builder.HasIndex(d => new { d.TenantId, d.ExpiryDate });
    }
}

internal sealed class ExpiryAlertConfiguration : IEntityTypeConfiguration<ExpiryAlert>
{
    public void Configure(EntityTypeBuilder<ExpiryAlert> builder)
    {
        builder.ToTable("expiry_alerts");
        builder.HasKey(a => new { a.ContractId, a.DaysBefore });
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
