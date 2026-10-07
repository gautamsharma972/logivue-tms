using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Persistence;

namespace Tms.Modules.Contracts.Infrastructure.Persistence.Configurations;

internal sealed class DphRuleConfiguration : IEntityTypeConfiguration<DphRule>
{
    public void Configure(EntityTypeBuilder<DphRule> builder)
    {
        builder.ToTable("dph_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Spec).HasJsonValue();
        builder.Ignore(r => r.Code);
        builder.HasIndex(r => new { r.ContractId, r.Version });
    }
}

internal sealed class DphPeriodSnapshotConfiguration : IEntityTypeConfiguration<DphPeriodSnapshot>
{
    public void Configure(EntityTypeBuilder<DphPeriodSnapshot> builder)
    {
        builder.ToTable("dph_snapshots");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.RuleCode).HasMaxLength(40).IsRequired();
        builder.Property(r => r.ReferencePrice).HasPrecision(8, 2);
        builder.Property(r => r.VariationPercent).HasPrecision(9, 4);
        builder.Property(r => r.AdjustmentPercent).HasPrecision(9, 4);
        builder.Property(r => r.CalculationVersion).HasMaxLength(10).IsRequired();
        builder.HasIndex(r => new { r.RuleId, r.PeriodStart }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.ContractId });
    }
}

internal sealed class AccessorialTypeConfiguration : IEntityTypeConfiguration<AccessorialType>
{
    public void Configure(EntityTypeBuilder<AccessorialType> builder)
    {
        builder.ToTable("accessorial_types");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Code).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Name).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(500);
        builder.Property(a => a.Calc).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Unit).HasMaxLength(20).IsRequired();
        builder.HasIndex(a => new { a.TenantId, a.Code }).IsUnique();
    }
}

internal sealed class ContractAccessorialConfiguration : IEntityTypeConfiguration<ContractAccessorial>
{
    public void Configure(EntityTypeBuilder<ContractAccessorial> builder)
    {
        builder.ToTable("contract_accessorials");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Spec).HasJsonValue();
        builder.Ignore(a => a.Code);
    }
}

internal sealed class ContractCapacityConfiguration : IEntityTypeConfiguration<ContractCapacity>
{
    public void Configure(EntityTypeBuilder<ContractCapacity> builder)
    {
        builder.ToTable("contract_capacity");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Spec).HasJsonValue();
    }
}

internal sealed class ContractSlaConfiguration : IEntityTypeConfiguration<ContractSla>
{
    public void Configure(EntityTypeBuilder<ContractSla> builder)
    {
        builder.ToTable("contract_sla");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Spec).HasJsonValue();
    }
}

internal sealed class FreightRatingConfiguration : IEntityTypeConfiguration<FreightRating>
{
    public void Configure(EntityTypeBuilder<FreightRating> builder)
    {
        builder.ToTable("ratings");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Reference).HasMaxLength(20).IsRequired();
        builder.Property(r => r.ShipmentReference).HasMaxLength(64);
        builder.Property(r => r.ErrorCode).HasMaxLength(40);
        builder.Property(r => r.Message).HasMaxLength(1000);
        builder.Property(r => r.ContractReference).HasMaxLength(20);
        builder.Property(r => r.RateCode).HasMaxLength(40);
        builder.Property(r => r.DphRuleCode).HasMaxLength(40);
        builder.Property(r => r.Lane).HasMaxLength(250).IsRequired();
        builder.Property(r => r.Service).HasConversion<string>().HasMaxLength(16);
        builder.Property(r => r.WeightKg).HasPrecision(12, 2);
        builder.Property(r => r.VolumeCbm).HasPrecision(10, 2);
        builder.Property(r => r.DistanceKm).HasPrecision(9, 2);
        builder.Property(r => r.BaseFreight).HasPrecision(14, 2);
        builder.Property(r => r.DphAdjustment).HasPrecision(14, 2);
        builder.Property(r => r.AccessorialAmount).HasPrecision(14, 2);
        builder.Property(r => r.DiscountAmount).HasPrecision(14, 2);
        builder.Property(r => r.TotalFreight).HasPrecision(14, 2);
        builder.Property(r => r.OverrideAmount).HasPrecision(14, 2);
        builder.Property(r => r.OverrideReason).HasMaxLength(500);
        builder.Property(r => r.OverrideApprovedBy).HasMaxLength(200);
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.CalculationVersion).HasMaxLength(10).IsRequired();
        builder.Property(r => r.InputJson).HasColumnType("longtext").IsRequired();
        builder.Property(r => r.TraceJson).HasColumnType("longtext").IsRequired();
        builder.Property(r => r.ReasonsJson).HasColumnType("longtext").IsRequired();
        builder.Ignore(r => r.EffectiveFreight);

        builder.HasMany(r => r.Components).WithOne().HasForeignKey(c => c.RatingId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Components).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(r => r.Exclusions).WithOne().HasForeignKey(c => c.RatingId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Exclusions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(r => new { r.TenantId, r.Reference }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.ShipmentReference });
        builder.HasIndex(r => new { r.TenantId, r.TransporterId, r.CalculatedAt });
        builder.HasIndex(r => new { r.TenantId, r.Qualified, r.CalculatedAt });
        builder.HasIndex(r => new { r.TenantId, r.ContractId, r.RateCardId });
    }
}

internal sealed class RatingComponentConfiguration : IEntityTypeConfiguration<RatingComponent>
{
    public void Configure(EntityTypeBuilder<RatingComponent> builder)
    {
        builder.ToTable("rating_components");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Type).HasMaxLength(30).IsRequired();
        builder.Property(c => c.Description).HasMaxLength(500).IsRequired();
        builder.Property(c => c.Unit).HasMaxLength(20);
        builder.Property(c => c.Quantity).HasPrecision(14, 4);
        builder.Property(c => c.Rate).HasPrecision(14, 4);
        builder.Property(c => c.Amount).HasPrecision(14, 2);
        builder.Property(c => c.Reference).HasMaxLength(100);
        builder.HasIndex(c => new { c.RatingId, c.Sequence });
    }
}

internal sealed class RatingExclusionConfiguration : IEntityTypeConfiguration<RatingExclusion>
{
    public void Configure(EntityTypeBuilder<RatingExclusion> builder)
    {
        builder.ToTable("rating_exclusions");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.ContractReference).HasMaxLength(30).IsRequired();
        builder.Property(c => c.RateReference).HasMaxLength(60);
        builder.Property(c => c.ReasonCode).HasMaxLength(40).IsRequired();
        builder.Property(c => c.Reason).HasMaxLength(500).IsRequired();
        builder.HasIndex(c => c.RatingId);
    }
}

internal sealed class RateImportBatchConfiguration : IEntityTypeConfiguration<RateImportBatch>
{
    public void Configure(EntityTypeBuilder<RateImportBatch> builder)
    {
        builder.ToTable("import_batches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Reference).HasMaxLength(20).IsRequired();
        builder.Property(b => b.FileName).HasMaxLength(255).IsRequired();
        builder.Property(b => b.Mode).HasConversion<string>().HasMaxLength(10);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(b => b.ContractNumber).HasMaxLength(20);
        builder.HasMany(b => b.Rows).WithOne().HasForeignKey(r => r.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Rows).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(b => new { b.TenantId, b.Reference }).IsUnique();
        builder.HasIndex(b => new { b.TenantId, b.Status });
    }
}

internal sealed class RateImportRowConfiguration : IEntityTypeConfiguration<RateImportRow>
{
    public void Configure(EntityTypeBuilder<RateImportRow> builder)
    {
        builder.ToTable("import_rows");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Values).HasJsonValue();
        builder.Property(r => r.Issues).HasJsonValue();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(10);
        builder.HasIndex(r => new { r.BatchId, r.RowNumber });
    }
}
