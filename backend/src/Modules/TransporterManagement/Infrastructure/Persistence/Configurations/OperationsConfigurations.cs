using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Audit;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence.Configurations;

internal sealed class TenderConfiguration : IEntityTypeConfiguration<Tender>
{
    public void Configure(EntityTypeBuilder<Tender> b)
    {
        b.ToTable("tm_tenders");
        b.Property(x => x.TenderNumber).HasMaxLength(30).IsRequired();
        b.Property(x => x.LoadReference).HasMaxLength(50).IsRequired();
        b.Property(x => x.TenderType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.OfferedRate).HasPrecision(12, 2);
        b.Property(x => x.WeightKg).HasPrecision(10, 2);
        b.Property(x => x.VolumeM3).HasPrecision(10, 2);
        b.Property(x => x.ServiceType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(1000);
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.HasIndex(x => x.TenderNumber);
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.ResponseDeadline);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TenderResponseConfiguration : IEntityTypeConfiguration<TenderResponse>
{
    public void Configure(EntityTypeBuilder<TenderResponse> b)
    {
        b.ToTable("tm_tender_responses");
        b.Property(x => x.Response).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.QuotedRate).HasPrecision(12, 2);
        b.Property(x => x.DriverReference).HasMaxLength(60);
        b.Property(x => x.DriverMobile).HasMaxLength(30);
        b.Property(x => x.Reason).HasMaxLength(60);
        b.Property(x => x.Comments).HasMaxLength(1000);
        b.HasIndex(x => x.TenderId);
        b.HasOne<Tender>().WithMany().HasForeignKey(x => x.TenderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TenderEventConfiguration : IEntityTypeConfiguration<TenderEvent>
{
    public void Configure(EntityTypeBuilder<TenderEvent> b)
    {
        b.ToTable("tm_tender_events");
        b.Property(x => x.EventType).HasMaxLength(50).IsRequired();
        b.Property(x => x.PerformedBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.Comments).HasMaxLength(1000);
        b.HasIndex(x => x.TenderId);
        b.HasOne<Tender>().WithMany().HasForeignKey(x => x.TenderId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VehiclePlacementRequestConfiguration : IEntityTypeConfiguration<VehiclePlacementRequest>
{
    public void Configure(EntityTypeBuilder<VehiclePlacementRequest> b)
    {
        b.ToTable("tm_vehicle_placement_requests");
        b.Property(x => x.LoadReference).HasMaxLength(50).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ExceptionReason).HasMaxLength(500);
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => x.LoadReference);
        b.HasIndex(x => x.Status);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class VehiclePlacementEventConfiguration : IEntityTypeConfiguration<VehiclePlacementEvent>
{
    public void Configure(EntityTypeBuilder<VehiclePlacementEvent> b)
    {
        b.ToTable("tm_vehicle_placement_events");
        b.Property(x => x.EventType).HasMaxLength(50).IsRequired();
        b.Property(x => x.Remarks).HasMaxLength(500);
        b.HasIndex(x => x.PlacementRequestId);
        b.HasOne<VehiclePlacementRequest>().WithMany().HasForeignKey(x => x.PlacementRequestId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PerformanceKpiConfiguration : IEntityTypeConfiguration<PerformanceKpi>
{
    public void Configure(EntityTypeBuilder<PerformanceKpi> b)
    {
        b.ToTable("tm_transporter_performance_kpis");
        b.Property(x => x.ServiceType).HasMaxLength(50);
        b.Property(x => x.KpiType).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Numerator).HasPrecision(14, 2);
        b.Property(x => x.Denominator).HasPrecision(14, 2);
        b.Property(x => x.KpiValue).HasPrecision(7, 2);
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => x.LaneReference);
        b.HasIndex(x => new { x.TransporterId, x.KpiType, x.PeriodStart, x.PeriodEnd });
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterScorecardConfiguration : IEntityTypeConfiguration<TransporterScorecard>
{
    public void Configure(EntityTypeBuilder<TransporterScorecard> b)
    {
        b.ToTable("tm_transporter_scorecards");
        b.Property(x => x.ScorecardProfile).HasMaxLength(50).IsRequired();
        b.Property(x => x.OverallScore).HasPrecision(6, 2);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.TransporterId, x.PeriodStart, x.PeriodEnd });
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ScorecardDetailConfiguration : IEntityTypeConfiguration<ScorecardDetail>
{
    public void Configure(EntityTypeBuilder<ScorecardDetail> b)
    {
        b.ToTable("tm_transporter_scorecard_details");
        b.Property(x => x.KpiType).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.KpiValue).HasPrecision(7, 2);
        b.Property(x => x.Numerator).HasPrecision(14, 2);
        b.Property(x => x.Denominator).HasPrecision(14, 2);
        b.Property(x => x.Weight).HasPrecision(5, 2);
        b.Property(x => x.WeightedScore).HasPrecision(7, 2);
        b.Property(x => x.BenchmarkValue).HasPrecision(7, 2);
        b.Property(x => x.Trend).HasPrecision(7, 2);
        b.HasIndex(x => x.ScorecardId);
        b.HasOne<TransporterScorecard>().WithMany().HasForeignKey(x => x.ScorecardId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TransporterRankingConfiguration : IEntityTypeConfiguration<TransporterRanking>
{
    public void Configure(EntityTypeBuilder<TransporterRanking> b)
    {
        b.ToTable("tm_transporter_rankings");
        b.Property(x => x.ServiceType).HasMaxLength(50);
        b.Property(x => x.Score).HasPrecision(6, 2);
        b.HasIndex(x => new { x.TransporterId, x.PeriodStart, x.PeriodEnd });
        b.HasIndex(x => new { x.LaneReference, x.PeriodStart, x.PeriodEnd, x.Rank });
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterAlertConfiguration : IEntityTypeConfiguration<TransporterAlert>
{
    public void Configure(EntityTypeBuilder<TransporterAlert> b)
    {
        b.ToTable("tm_transporter_alerts");
        b.Property(x => x.AlertType).HasMaxLength(60).IsRequired();
        b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.LoadReference).HasMaxLength(50);
        b.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(60).IsRequired();
        b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Severity);
        b.HasIndex(x => x.DueAt);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterExceptionConfiguration : IEntityTypeConfiguration<TransporterException>
{
    public void Configure(EntityTypeBuilder<TransporterException> b)
    {
        b.ToTable("tm_transporter_exceptions");
        b.Property(x => x.ExceptionType).HasMaxLength(60).IsRequired();
        b.Property(x => x.Severity).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.LoadReference).HasMaxLength(50);
        b.Property(x => x.RootCause).HasMaxLength(500);
        b.Property(x => x.Owner).HasMaxLength(100);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ActionTaken).HasMaxLength(1000);
        b.HasIndex(x => x.TransporterId);
        b.HasIndex(x => x.Status);
        b.HasIndex(x => x.Severity);
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PlanningFeedbackConfiguration : IEntityTypeConfiguration<TransporterPlanningFeedback>
{
    public void Configure(EntityTypeBuilder<TransporterPlanningFeedback> b)
    {
        b.ToTable("tm_transporter_planning_feedback");
        foreach (var property in b.Metadata.GetProperties().Where(p => p.ClrType == typeof(decimal?)))
        {
            b.Property(property.Name).HasPrecision(7, 2);
        }
        b.HasIndex(x => new { x.TransporterId, x.LaneReference }).IsUnique();
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PlanningRuleConfiguration : IEntityTypeConfiguration<TransporterPlanningRule>
{
    public void Configure(EntityTypeBuilder<TransporterPlanningRule> b)
    {
        b.ToTable("tm_transporter_planning_rules");
        b.Property(x => x.RuleType).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Reason).HasMaxLength(500).IsRequired();
        b.Property(x => x.CreatedBy).HasMaxLength(100);
        b.HasIndex(x => new { x.TransporterId, x.RuleType });
        b.HasOne<Transporter>().WithMany().HasForeignKey(x => x.TransporterId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class TransporterAuditLogConfiguration : IEntityTypeConfiguration<TransporterAuditLog>
{
    public void Configure(EntityTypeBuilder<TransporterAuditLog> b)
    {
        b.ToTable("tm_transporter_audit_logs");
        b.Property(x => x.EntityType).HasMaxLength(60).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(60).IsRequired();
        b.Property(x => x.Action).HasMaxLength(80).IsRequired();
        b.Property(x => x.OldValueJson).HasColumnType("json");
        b.Property(x => x.NewValueJson).HasColumnType("json");
        b.Property(x => x.PerformedBy).HasMaxLength(100).IsRequired();
        b.Property(x => x.Reason).HasMaxLength(1000);
        b.Property(x => x.CorrelationId).HasMaxLength(100);
        b.HasIndex(x => new { x.EntityType, x.EntityId });
        b.HasIndex(x => x.PerformedAt);
    }
}

internal sealed class LookupConfiguration : IEntityTypeConfiguration<TransporterType>, IEntityTypeConfiguration<CapabilityType>, IEntityTypeConfiguration<DocumentType>
{
    public void Configure(EntityTypeBuilder<TransporterType> b)
    {
        b.ToTable("tm_transporter_types");
        b.Property(x => x.Code).HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasData(
            new TransporterType { Id = 1, Code = "FTL", Name = "Full Truck Load" },
            new TransporterType { Id = 2, Code = "PTL", Name = "Part Truck Load" },
            new TransporterType { Id = 3, Code = "EXPRESS", Name = "Express" },
            new TransporterType { Id = 4, Code = "DEDICATED", Name = "Dedicated" },
            new TransporterType { Id = 5, Code = "LAST_MILE", Name = "Last Mile" },
            new TransporterType { Id = 6, Code = "MILK_RUN", Name = "Milk Run" },
            new TransporterType { Id = 7, Code = "FLEET_OWNER", Name = "Fleet Owner" },
            new TransporterType { Id = 8, Code = "BROKER", Name = "Broker" },
            new TransporterType { Id = 9, Code = "3PL", Name = "Third-Party Logistics" });
    }

    public void Configure(EntityTypeBuilder<CapabilityType> b)
    {
        b.ToTable("tm_capability_types");
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasData(
            new CapabilityType { Id = 1, Code = "FTL", Name = "FTL" },
            new CapabilityType { Id = 2, Code = "PTL", Name = "PTL" },
            new CapabilityType { Id = 3, Code = "HAZARDOUS", Name = "Hazardous" },
            new CapabilityType { Id = 4, Code = "TEMP_CONTROLLED", Name = "Temperature Controlled" },
            new CapabilityType { Id = 5, Code = "FRAGILE", Name = "Fragile" },
            new CapabilityType { Id = 6, Code = "HEAVY_CARGO", Name = "Heavy Cargo" },
            new CapabilityType { Id = 7, Code = "HIGH_VALUE", Name = "High Value" },
            new CapabilityType { Id = 8, Code = "REVERSE_LOGISTICS", Name = "Reverse Logistics" },
            new CapabilityType { Id = 9, Code = "MILK_RUN", Name = "Milk Run" },
            new CapabilityType { Id = 10, Code = "EXPRESS", Name = "Express" },
            new CapabilityType { Id = 11, Code = "DEDICATED", Name = "Dedicated" });
    }

    public void Configure(EntityTypeBuilder<DocumentType> b)
    {
        b.ToTable("tm_document_types");
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasIndex(x => x.Code).IsUnique();
        b.HasData(
            new DocumentType { Id = 1, Code = "GST_CERT", Name = "GST Certificate", IsMandatory = true, ExpiryRequired = false, RenewalReminderDays = 30 },
            new DocumentType { Id = 2, Code = "PAN", Name = "PAN", IsMandatory = true, ExpiryRequired = false },
            new DocumentType { Id = 3, Code = "COMPANY_REG", Name = "Company Registration", IsMandatory = true, ExpiryRequired = false },
            new DocumentType { Id = 4, Code = "INSURANCE", Name = "Insurance", IsMandatory = true, IsTransporterLevel = false, IsVehicleLevel = true, ExpiryRequired = true, RenewalReminderDays = 30, BlockAllocationWhenExpired = true },
            new DocumentType { Id = 5, Code = "PERMIT", Name = "Permit", IsVehicleLevel = true, ExpiryRequired = true, RenewalReminderDays = 30, BlockAllocationWhenExpired = true },
            new DocumentType { Id = 6, Code = "FITNESS", Name = "Fitness Certificate", IsVehicleLevel = true, ExpiryRequired = true, RenewalReminderDays = 30, BlockAllocationWhenExpired = true },
            new DocumentType { Id = 7, Code = "PUC", Name = "PUC", IsVehicleLevel = true, ExpiryRequired = true, RenewalReminderDays = 15, BlockAllocationWhenExpired = false },
            new DocumentType { Id = 8, Code = "VEHICLE_RC", Name = "Vehicle RC", IsVehicleLevel = true, IsTransporterLevel = false, ExpiryRequired = false },
            new DocumentType { Id = 9, Code = "DRIVER_LICENCE", Name = "Driver Licence", IsDriverLevel = true, IsTransporterLevel = false, ExpiryRequired = true, RenewalReminderDays = 30, BlockAllocationWhenExpired = true },
            new DocumentType { Id = 10, Code = "OTHER", Name = "Other", IsTransporterLevel = true, ExpiryRequired = false });
    }
}

internal sealed class ConfigurationSettingConfiguration : IEntityTypeConfiguration<ConfigurationSetting>
{
    private static readonly DateTime SeededAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<ConfigurationSetting> b)
    {
        b.ToTable("tm_configuration_settings");
        b.Property(x => x.Key).HasMaxLength(100).IsRequired();
        b.Property(x => x.ValueJson).HasColumnType("json").IsRequired();
        b.Property(x => x.UpdatedBy).HasMaxLength(100);
        b.HasIndex(x => x.Key).IsUnique();

        b.HasData(
            Setting(1, SettingKeys.KpiWeightsDefault,
                """{"OnTimePickup":10,"OnTimeDelivery":25,"PlacementCompliance":15,"TenderAcceptance":10,"PodCompliance":5,"ClaimsRate":10,"CostPerformance":15,"Availability":10}"""),
            Setting(2, SettingKeys.ScorecardMinimumSampleSize, "20"),
            Setting(3, SettingKeys.TenderResponseSlaMinutes, "30"),
            Setting(4, SettingKeys.PlacementAlertMinutesBefore, "30"),
            Setting(5, SettingKeys.PodSubmissionSlaHours, "24"),
            Setting(6, SettingKeys.ComplianceRenewalReminderDays, "30"),
            Setting(7, SettingKeys.PerformanceThresholds,
                """{"OnTimeDelivery":{"Excellent":95,"Acceptable":90},"PlacementCompliance":{"Excellent":95,"Acceptable":90},"PodCompliance":{"Excellent":98,"Acceptable":95}}"""),
            Setting(8, SettingKeys.AlertsEnabled, "true"),
            Setting(9, SettingKeys.DelayEscalationMinutes, """{"CoordinatorMinutes":0,"ManagerAfterMinutes":60,"HeadAfterMinutes":120}"""),
            Setting(10, SettingKeys.RecommendationWeights,
                """{"Rate":30,"OnTimePickup":20,"OnTimeDelivery":15,"PlacementCompliance":10,"PodCompliance":5,"TenderAcceptance":5,"ClaimsRate":5,"Availability":10}"""),
            Setting(11, SettingKeys.RecommendationScoring,
                """{"ClaimsZeroAtPct":5,"AvailabilityPerVehicle":40,"PreferredBonus":5,"InsufficientDataScore":60}"""),
            Setting(12, SettingKeys.PerformanceWindowDays, "90"),
            Setting(13, SettingKeys.EligibilityRestrictions, """{"Enabled":false,"MinOtdPct":90,"MaxClaimsRatePct":3}"""),
            Setting(14, SettingKeys.TenderRejectionReasons,
                """["VEHICLE_UNAVAILABLE","LANE_UNAVAILABLE","RATE_ISSUE","PICKUP_TIME_ISSUE","CAPACITY_UNAVAILABLE","DESTINATION_ISSUE","OTHER"]"""),
            Setting(15, SettingKeys.TenderAllowCounterOffer, "false"),
            Setting(16, SettingKeys.PlacementGraceMinutes, "15"),
            Setting(17, SettingKeys.ExecutionDelayPolicy,
                """{"ToleranceMinutes":15,"Reasons":[{"Code":"TRANSPORTER_DELAY","Name":"Transporter Delay","Attribution":"Carrier"},{"Code":"VEHICLE_BREAKDOWN","Name":"Vehicle Breakdown","Attribution":"Carrier"},{"Code":"DOCUMENTATION_ISSUE","Name":"Documentation Issue","Attribution":"Carrier"},{"Code":"CUSTOMER_DELAY","Name":"Customer Delay","Attribution":"NonCarrier"},{"Code":"WAREHOUSE_DELAY","Name":"Warehouse Delay","Attribution":"NonCarrier"},{"Code":"TRAFFIC","Name":"Traffic","Attribution":"NonCarrier"},{"Code":"ROUTE_RESTRICTION","Name":"Route Restriction","Attribution":"NonCarrier"},{"Code":"WEATHER","Name":"Weather","Attribution":"NonCarrier"},{"Code":"FORCE_MAJEURE","Name":"Force Majeure","Attribution":"NonCarrier"},{"Code":"OTHER","Name":"Other","Attribution":"Unattributed"}]}"""));
    }

    private static ConfigurationSetting Setting(long id, string key, string json) => new()
    {
        Id = id,
        Key = key,
        ValueJson = json,
        Version = 1,
        UpdatedAt = SeededAt,
        UpdatedBy = "seed"
    };
}
