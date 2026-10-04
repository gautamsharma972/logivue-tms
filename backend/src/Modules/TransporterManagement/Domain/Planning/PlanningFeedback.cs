namespace LogiVue.Tms.TransporterManagement.Domain.Planning;

/// <summary>
/// Read-optimised planning profile per transporter (and optionally lane), refreshed when source data changes.
/// The authoritative KPI data remains in the performance tables.
/// </summary>
public class TransporterPlanningFeedback
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public long? LaneReference { get; set; }
    public decimal? OverallScore { get; set; }
    public decimal? OtpPct { get; set; }
    public decimal? OtdPct { get; set; }
    public decimal? PlacementCompliancePct { get; set; }
    public decimal? PodCompliancePct { get; set; }
    public decimal? TenderAcceptancePct { get; set; }
    public decimal? ClaimsRatePct { get; set; }
    public decimal? CostPerformanceScore { get; set; }
    public decimal? AvailabilityScore { get; set; }
    public bool PreferredFlag { get; set; }
    public bool RestrictedFlag { get; set; }
    public decimal? RecommendationScore { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>A configurable, auditable planning classification (preferred, restricted, etc.).</summary>
public class TransporterPlanningRule
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public Common.PlanningRuleType RuleType { get; set; }
    public long? LaneReference { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime EffectiveFrom { get; set; }
    public DateTime? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
    public string CreatedBy { get; set; } = string.Empty;
}
