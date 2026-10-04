using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Persistence;

namespace Tms.Modules.Shipments.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.Number).HasMaxLength(20).IsRequired();
        builder.Property(o => o.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(o => o.Reference).HasMaxLength(64);
        builder.Property(o => o.Pickup).HasJsonValue();
        builder.Property(o => o.Drop).HasJsonValue();
        builder.Property(o => o.PickupState).HasMaxLength(100).IsRequired();
        builder.Property(o => o.PickupCity).HasMaxLength(100).IsRequired();
        builder.Property(o => o.DropState).HasMaxLength(100).IsRequired();
        builder.Property(o => o.DropCity).HasMaxLength(100).IsRequired();
        builder.Property(o => o.WeightKg).HasPrecision(12, 2);
        builder.Property(o => o.VolumeCbm).HasPrecision(10, 3);
        builder.Property(o => o.Description).HasMaxLength(300).IsRequired();
        builder.Property(o => o.Notes).HasMaxLength(1000);
        builder.Property(o => o.CancelReason).HasMaxLength(500);

        builder.HasIndex(o => new { o.TenantId, o.Number }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.Status, o.PickupState, o.PickupCity }); // the planning board
        builder.HasIndex(o => new { o.TenantId, o.ShipmentId });
    }
}

internal sealed class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Number).HasMaxLength(20).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(12);
        builder.Property(s => s.Mode).HasConversion<string>().HasMaxLength(4);
        builder.Property(s => s.OriginState).HasMaxLength(100).IsRequired();
        builder.Property(s => s.OriginCity).HasMaxLength(100).IsRequired();
        builder.Property(s => s.CollectionState).HasMaxLength(100);
        builder.Property(s => s.CollectionCity).HasMaxLength(100);
        builder.Property(s => s.DistanceKm).HasPrecision(9, 2);
        builder.Property(s => s.TotalWeightKg).HasPrecision(12, 2);
        builder.Property(s => s.TotalVolumeCbm).HasPrecision(10, 3);
        builder.Property(s => s.PeakOnboardKg).HasPrecision(12, 2);
        builder.Property(s => s.PlanReference).HasMaxLength(40);
        builder.Property(s => s.PlannedCost).HasPrecision(18, 2);
        builder.Property(s => s.FreightEstimate).HasPrecision(18, 2);
        builder.Property(s => s.EstimateLines).HasJsonValue();
        builder.Property(s => s.ContractReference).HasMaxLength(30);
        builder.Property(s => s.OverrideReason).HasMaxLength(500);
        builder.Property(s => s.LastRejectionReason).HasMaxLength(500);
        builder.Property(s => s.VehicleRegistration).HasMaxLength(12);
        builder.Property(s => s.DriverName).HasMaxLength(150);
        builder.Property(s => s.DriverPhone).HasMaxLength(15);
        builder.Property(s => s.CancelReason).HasMaxLength(500);
        builder.Ignore(s => s.OrdersInDropSequence);
        builder.Ignore(s => s.Utilization);

        builder.HasMany(s => s.Orders).WithOne().HasForeignKey(o => o.ShipmentId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Orders).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(s => new { s.TenantId, s.Number }).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.Status });
        builder.HasIndex(s => new { s.TenantId, s.TransporterId, s.Status }); // the vendor portal's worklist
    }
}

internal sealed class ShipmentOrderConfiguration : IEntityTypeConfiguration<ShipmentOrder>
{
    public void Configure(EntityTypeBuilder<ShipmentOrder> builder)
    {
        builder.ToTable("shipment_orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedNever();
        builder.Property(o => o.LrNumber).HasMaxLength(20);
        builder.Property(o => o.Direction).HasConversion<string>().HasMaxLength(10).HasDefaultValue(OrderDirection.Forward);
        builder.Property(o => o.ReceiverName).HasMaxLength(150);
        builder.Property(o => o.DeliveryRemarks).HasMaxLength(500);
        builder.Property(o => o.PodStatus).HasConversion<string>().HasMaxLength(10).HasDefaultValue(PodStatus.Awaiting);
        builder.Property(o => o.PodRejectionReason).HasMaxLength(500);
        builder.Ignore(o => o.IsDelivered);
        builder.Ignore(o => o.ShortagePackages);
        builder.Ignore(o => o.HasException);
        builder.HasIndex(o => new { o.TenantId, o.PodStatus, o.DeliveredAt }); // the proof-of-delivery worklist and ageing
        builder.HasIndex(o => new { o.ShipmentId, o.OrderId }).IsUnique();
        builder.HasIndex(o => new { o.TenantId, o.OrderId });
    }
}

internal sealed class PlanningRunConfiguration : IEntityTypeConfiguration<PlanningRun>
{
    public void Configure(EntityTypeBuilder<PlanningRun> builder)
    {
        builder.ToTable("planning_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Number).HasMaxLength(24).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Reason).HasMaxLength(500);
        builder.Property(r => r.CancelReason).HasMaxLength(500);
        builder.Property(r => r.Options).HasJsonValue();
        builder.Property(r => r.OrderIds).HasJsonValue();
        builder.Property(r => r.Plan).HasJsonValue();
        builder.Property(r => r.Log).HasJsonValue();

        builder.HasIndex(r => new { r.TenantId, r.RunGroupId, r.PlanVersion }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.Number, r.PlanVersion }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.Status, r.PlanningDate });
    }
}

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.Code).HasMaxLength(30).IsRequired();
        builder.Property(l => l.Name).HasMaxLength(200).IsRequired();
        builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(12);
        builder.Property(l => l.Line1).HasMaxLength(200).IsRequired();
        builder.Property(l => l.City).HasMaxLength(100).IsRequired();
        builder.Property(l => l.State).HasMaxLength(100).IsRequired();
        builder.Property(l => l.Pincode).HasMaxLength(6).IsRequired();
        builder.Ignore(l => l.Point);

        builder.HasIndex(l => new { l.TenantId, l.Code }).IsUnique();
        builder.HasIndex(l => new { l.TenantId, l.City });
    }
}

internal sealed class MilkRunTemplateConfiguration : IEntityTypeConfiguration<MilkRunTemplate>
{
    public void Configure(EntityTypeBuilder<MilkRunTemplate> builder)
    {
        builder.ToTable("milk_run_templates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Code).HasMaxLength(30).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Days).HasJsonValue();
        builder.Ignore(t => t.StopsInSequence);

        builder.HasMany(t => t.Stops).WithOne().HasForeignKey(s => s.MilkRunTemplateId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Stops).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(t => new { t.TenantId, t.Code }).IsUnique();
        builder.HasIndex(t => new { t.TenantId, t.IsActive });
    }
}

internal sealed class MilkRunStopConfiguration : IEntityTypeConfiguration<MilkRunStop>
{
    public void Configure(EntityTypeBuilder<MilkRunStop> builder)
    {
        builder.ToTable("milk_run_stops");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Type).HasConversion<string>().HasMaxLength(10);
        builder.HasIndex(s => new { s.MilkRunTemplateId, s.Sequence }).IsUnique();
        builder.HasIndex(s => new { s.TenantId, s.LocationId });
    }
}

internal sealed class PodDocumentConfiguration : IEntityTypeConfiguration<PodDocument>
{
    public void Configure(EntityTypeBuilder<PodDocument> builder)
    {
        builder.ToTable("pod_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.FileKey).HasMaxLength(300).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(255).IsRequired();
        builder.Property(d => d.ContentType).HasMaxLength(100).IsRequired();
        builder.HasIndex(d => new { d.TenantId, d.ShipmentId, d.OrderId });
        builder.HasIndex(d => new { d.TenantId, d.TransporterId });
    }
}

internal sealed class ProductCompatibilityRuleConfiguration : IEntityTypeConfiguration<ProductCompatibilityRule>
{
    public void Configure(EntityTypeBuilder<ProductCompatibilityRule> builder)
    {
        builder.ToTable("compatibility_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.CategoryA).HasMaxLength(50).IsRequired();
        builder.Property(r => r.CategoryB).HasMaxLength(50).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(300);
        builder.HasIndex(r => new { r.TenantId, r.CategoryA, r.CategoryB }).IsUnique();
    }
}
