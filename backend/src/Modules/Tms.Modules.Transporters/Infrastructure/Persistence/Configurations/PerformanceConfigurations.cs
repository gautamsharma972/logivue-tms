using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Persistence;

namespace Tms.Modules.Transporters.Infrastructure.Persistence.Configurations;

internal sealed class TenderInvitationConfiguration : IEntityTypeConfiguration<TenderInvitation>
{
    public void Configure(EntityTypeBuilder<TenderInvitation> builder)
    {
        builder.ToTable("tender_invitations");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(t => t.Outcome).HasConversion<string>().HasMaxLength(10);
        builder.Property(t => t.Mode).HasConversion<string?>().HasMaxLength(4);
        builder.Property(t => t.Reason).HasMaxLength(500);
        builder.Property(t => t.OriginState).HasMaxLength(100);
        builder.Property(t => t.OriginCity).HasMaxLength(100);
        builder.Property(t => t.DestinationState).HasMaxLength(100);
        builder.Property(t => t.DestinationCity).HasMaxLength(100);
        builder.HasIndex(t => new { t.TenantId, t.ShipmentId, t.SentAt }).IsUnique(); // a re-delivered event cannot add a second row
        builder.HasIndex(t => new { t.TenantId, t.TransporterId, t.SentAt });
    }
}

internal sealed class LoadExecutionConfiguration : IEntityTypeConfiguration<LoadExecution>
{
    public void Configure(EntityTypeBuilder<LoadExecution> builder)
    {
        builder.ToTable("load_executions");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(e => e.Mode).HasConversion<string>().HasMaxLength(4);
        builder.Property(e => e.OriginState).HasMaxLength(100);
        builder.Property(e => e.OriginCity).HasMaxLength(100);
        builder.Property(e => e.DestinationState).HasMaxLength(100);
        builder.Property(e => e.DestinationCity).HasMaxLength(100);
        builder.Property(e => e.PickupDelayReasonCode).HasMaxLength(40);
        builder.Property(e => e.DeliveryDelayReasonCode).HasMaxLength(40);
        builder.Property(e => e.PickupAttribution).HasConversion<string>().HasMaxLength(14);
        builder.Property(e => e.DeliveryAttribution).HasConversion<string>().HasMaxLength(14);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(12);
        builder.HasMany(e => e.Events).WithOne().HasForeignKey(e => e.LoadExecutionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(e => e.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(e => new { e.TenantId, e.ShipmentId }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.TransporterId, e.PlannedPickupAt });
    }
}

internal sealed class ExecutionEventConfiguration : IEntityTypeConfiguration<ExecutionEvent>
{
    public void Configure(EntityTypeBuilder<ExecutionEvent> builder)
    {
        builder.ToTable("execution_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EventType).HasConversion<string>().HasMaxLength(24);
        builder.Property(e => e.DelayReasonCode).HasMaxLength(40);
        builder.Property(e => e.Remarks).HasMaxLength(500);
    }
}

internal sealed class PerformanceKpiConfiguration : IEntityTypeConfiguration<PerformanceKpi>
{
    public void Configure(EntityTypeBuilder<PerformanceKpi> builder)
    {
        builder.ToTable("performance_kpis");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id).ValueGeneratedNever();
        builder.Property(k => k.KpiType).HasConversion<string>().HasMaxLength(24);
        builder.Property(k => k.Numerator).HasPrecision(14, 2);
        builder.Property(k => k.Denominator).HasPrecision(14, 2);
        builder.Property(k => k.KpiValue).HasPrecision(7, 2);
        builder.HasIndex(k => new { k.TenantId, k.TransporterId, k.PeriodStart });
        builder.HasIndex(k => new { k.TenantId, k.LaneId, k.PeriodStart });
        builder.HasIndex(k => new { k.TenantId, k.VehicleTypeId, k.PeriodStart });
    }
}

internal sealed class ScorecardConfiguration : IEntityTypeConfiguration<Scorecard>
{
    public void Configure(EntityTypeBuilder<Scorecard> builder)
    {
        builder.ToTable("scorecards");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.OverallScore).HasPrecision(7, 2);
        builder.Property(s => s.Lines).HasJsonValue();
        builder.HasIndex(s => new { s.TenantId, s.TransporterId, s.GeneratedAt });
    }
}

internal sealed class TransporterLaneConfiguration : IEntityTypeConfiguration<TransporterLane>
{
    public void Configure(EntityTypeBuilder<TransporterLane> builder)
    {
        builder.ToTable("lanes");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.OriginState).HasMaxLength(100).IsRequired();
        builder.Property(l => l.OriginCity).HasMaxLength(100);
        builder.Property(l => l.DestinationState).HasMaxLength(100).IsRequired();
        builder.Property(l => l.DestinationCity).HasMaxLength(100);
        builder.Property(l => l.Mode).HasConversion<string?>().HasMaxLength(4);
        builder.HasIndex(l => new { l.TenantId, l.TransporterId, l.IsActive });
    }
}

internal sealed class TransporterSettingConfiguration : IEntityTypeConfiguration<TransporterSetting>
{
    public void Configure(EntityTypeBuilder<TransporterSetting> builder)
    {
        builder.ToTable("settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Key).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ValueJson).HasColumnType("json");
        builder.HasIndex(s => new { s.TenantId, s.Key }).IsUnique();
    }
}

internal sealed class TransporterCapabilityConfiguration : IEntityTypeConfiguration<TransporterCapability>
{
    public void Configure(EntityTypeBuilder<TransporterCapability> builder)
    {
        builder.ToTable("capabilities");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Code).HasMaxLength(40).IsRequired();
        builder.HasIndex(c => new { c.TenantId, c.TransporterId, c.Code });
    }
}

internal sealed class PlanningRuleConfiguration : IEntityTypeConfiguration<PlanningRule>
{
    public void Configure(EntityTypeBuilder<PlanningRule> builder)
    {
        builder.ToTable("planning_rules");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.RuleType).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Reason).HasMaxLength(300).IsRequired();
        builder.Property(r => r.EndedBecause).HasMaxLength(300);
        builder.HasIndex(r => new { r.TenantId, r.TransporterId, r.IsActive });
    }
}

internal sealed class PlanningFeedbackConfiguration : IEntityTypeConfiguration<PlanningFeedback>
{
    public void Configure(EntityTypeBuilder<PlanningFeedback> builder)
    {
        builder.ToTable("planning_feedback");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).ValueGeneratedNever();
        foreach (var column in new[] { nameof(PlanningFeedback.OverallScore), nameof(PlanningFeedback.OtpPct), nameof(PlanningFeedback.OtdPct), nameof(PlanningFeedback.PlacementCompliancePct), nameof(PlanningFeedback.PodCompliancePct), nameof(PlanningFeedback.TenderAcceptancePct), nameof(PlanningFeedback.ClaimsRatePct) })
        {
            builder.Property<decimal?>(column).HasPrecision(7, 2);
        }

        builder.HasIndex(f => new { f.TenantId, f.TransporterId }).IsUnique();
    }
}

internal sealed class VehiclePlacementConfiguration : IEntityTypeConfiguration<VehiclePlacement>
{
    public void Configure(EntityTypeBuilder<VehiclePlacement> builder)
    {
        builder.ToTable("placements");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Mode).HasConversion<string>().HasMaxLength(4);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(p => p.OriginState).HasMaxLength(100);
        builder.Property(p => p.OriginCity).HasMaxLength(100);
        builder.Property(p => p.DestinationState).HasMaxLength(100);
        builder.Property(p => p.DestinationCity).HasMaxLength(100);
        builder.Property(p => p.VehicleRegistration).HasMaxLength(20);
        builder.Property(p => p.ExceptionReason).HasMaxLength(500);
        builder.HasMany(p => p.Events).WithOne().HasForeignKey(e => e.PlacementId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Events).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(p => new { p.TenantId, p.ShipmentId }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.TransporterId, p.RequiredAt });
        builder.HasIndex(p => new { p.TenantId, p.Status });
    }
}

internal sealed class PlacementEventConfiguration : IEntityTypeConfiguration<PlacementEvent>
{
    public void Configure(EntityTypeBuilder<PlacementEvent> builder)
    {
        builder.ToTable("placement_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.EventType).HasMaxLength(24).IsRequired();
        builder.Property(e => e.Remarks).HasMaxLength(500);
    }
}

internal sealed class ClaimRecordConfiguration : IEntityTypeConfiguration<ClaimRecord>
{
    public void Configure(EntityTypeBuilder<ClaimRecord> builder)
    {
        builder.ToTable("claims");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.ShipmentNumber).HasMaxLength(20);
        builder.Property(c => c.ClaimType).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.ClaimValue).HasPrecision(14, 2);
        builder.Property(c => c.Remarks).HasMaxLength(500);
        builder.Property(c => c.SourceKey).HasMaxLength(80);
        builder.HasIndex(c => new { c.TenantId, c.TransporterId, c.ClaimDate });
        builder.HasIndex(c => new { c.TenantId, c.SourceKey }).IsUnique();
    }
}

internal sealed class LoadCostConfiguration : IEntityTypeConfiguration<LoadCost>
{
    public void Configure(EntityTypeBuilder<LoadCost> builder)
    {
        builder.ToTable("load_costs");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.ShipmentNumber).HasMaxLength(20).IsRequired();
        builder.Property(c => c.AgreedAmount).HasPrecision(14, 2);
        builder.Property(c => c.InvoicedAmount).HasPrecision(14, 2);
        builder.Ignore(c => c.OnBudget);
        builder.HasIndex(c => new { c.TenantId, c.ShipmentId }).IsUnique(); // a load is counted once
        builder.HasIndex(c => new { c.TenantId, c.TransporterId, c.ServiceDate });
    }
}

internal sealed class CapacityDayConfiguration : IEntityTypeConfiguration<CapacityDay>
{
    public void Configure(EntityTypeBuilder<CapacityDay> builder)
    {
        builder.ToTable("capacity_days");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.HasIndex(c => new { c.TenantId, c.TransporterId, c.Date }).IsUnique();
    }
}

internal sealed class TransporterAlertConfiguration : IEntityTypeConfiguration<TransporterAlert>
{
    public void Configure(EntityTypeBuilder<TransporterAlert> builder)
    {
        builder.ToTable("alerts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.AlertType).HasMaxLength(40).IsRequired();
        builder.Property(a => a.Severity).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(14);
        builder.Property(a => a.ShipmentNumber).HasMaxLength(20);
        builder.Property(a => a.EntityKey).HasMaxLength(80).IsRequired();
        builder.Property(a => a.Message).HasMaxLength(500).IsRequired();
        builder.Property(a => a.Resolution).HasMaxLength(500);
        builder.HasIndex(a => new { a.TenantId, a.Status, a.Severity });
        builder.HasIndex(a => new { a.TenantId, a.AlertType, a.EntityKey });
    }
}

internal sealed class TransporterContactConfiguration : IEntityTypeConfiguration<TransporterContact>
{
    public void Configure(EntityTypeBuilder<TransporterContact> builder)
    {
        builder.ToTable("contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Designation).HasMaxLength(100);
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.Property(c => c.Phone).HasMaxLength(15);
        builder.Property(c => c.ContactType).HasMaxLength(50).IsRequired();
        builder.HasIndex(c => new { c.TenantId, c.TransporterId });
    }
}

internal sealed class TransporterBranchConfiguration : IEntityTypeConfiguration<TransporterBranch>
{
    public void Configure(EntityTypeBuilder<TransporterBranch> builder)
    {
        builder.ToTable("branches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.Code).HasMaxLength(30).IsRequired();
        builder.Property(b => b.Name).HasMaxLength(150).IsRequired();
        builder.Property(b => b.Address).HasMaxLength(300);
        builder.Property(b => b.City).HasMaxLength(100);
        builder.Property(b => b.State).HasMaxLength(100);
        builder.Property(b => b.ContactName).HasMaxLength(150);
        builder.Property(b => b.ContactPhone).HasMaxLength(15);
        builder.HasIndex(b => new { b.TenantId, b.TransporterId, b.Code }).IsUnique();
    }
}
