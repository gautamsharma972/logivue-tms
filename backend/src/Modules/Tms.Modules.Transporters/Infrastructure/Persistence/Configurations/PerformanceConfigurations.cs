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
