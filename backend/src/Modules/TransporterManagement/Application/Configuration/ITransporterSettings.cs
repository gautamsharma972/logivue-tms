namespace LogiVue.Tms.TransporterManagement.Application.Configuration;

/// <summary>
/// Reads business configuration (KPI weights, SLAs, thresholds) from the database.
/// Services depend on this, never on hard-coded policy values.
/// </summary>
public interface ITransporterSettings
{
    Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);
}

/// <summary>Well-known configuration keys. Values are seeded in the Infrastructure layer.</summary>
public static class SettingKeys
{
    public const string KpiWeightsDefault = "tm.scorecard.weights.default";
    public const string ScorecardMinimumSampleSize = "tm.scorecard.minimumSampleSize";
    public const string TenderResponseSlaMinutes = "tm.tender.responseSlaMinutes";
    public const string PlacementAlertMinutesBefore = "tm.placement.alertMinutesBefore";
    public const string PodSubmissionSlaHours = "tm.pod.submissionSlaHours";
    public const string ComplianceRenewalReminderDays = "tm.compliance.renewalReminderDays";
    public const string PerformanceThresholds = "tm.performance.thresholds";
    public const string AlertsEnabled = "tm.alerts.enabled";
    public const string DelayEscalationMinutes = "tm.alerts.delayEscalationMinutes";
    public const string RecommendationWeights = "tm.recommendation.weights";
    public const string RecommendationScoring = "tm.recommendation.scoring";
    public const string PerformanceWindowDays = "tm.performance.windowDays";
    public const string EligibilityRestrictions = "tm.eligibility.restrictions";
    public const string TenderRejectionReasons = "tm.tender.rejectionReasons";
    public const string TenderAllowCounterOffer = "tm.tender.allowCounterOffer";
    public const string PlacementGraceMinutes = "tm.placement.graceMinutes";
    public const string ExecutionDelayPolicy = "tm.execution.delayPolicy";
}

/// <summary>Shape of <see cref="SettingKeys.KpiWeightsDefault"/>. Weights are percentages and should sum to 100.</summary>
public record KpiWeightsSetting(
    decimal OnTimePickup,
    decimal OnTimeDelivery,
    decimal PlacementCompliance,
    decimal TenderAcceptance,
    decimal PodCompliance,
    decimal ClaimsRate,
    decimal CostPerformance,
    decimal Availability);

/// <summary>Weights for the carrier recommendation. Percentages; they are normalised, so they need not sum to 100.</summary>
public sealed record RecommendationWeightsSetting(
    decimal Rate,
    decimal OnTimePickup,
    decimal OnTimeDelivery,
    decimal PlacementCompliance,
    decimal PodCompliance,
    decimal TenderAcceptance,
    decimal ClaimsRate,
    decimal Availability);

/// <summary>Scoring parameters for the recommendation.</summary>
/// <param name="ClaimsZeroAtPct">Claims rate at which the claims score reaches zero.</param>
/// <param name="AvailabilityPerVehicle">Availability score awarded per available vehicle (capped at 100).</param>
/// <param name="PreferredBonus">Points added for a preferred carrier or lane.</param>
/// <param name="InsufficientDataScore">Neutral score for a factor with too little data. It neither rewards nor penalises.</param>
public sealed record RecommendationScoringSetting(
    decimal ClaimsZeroAtPct,
    decimal AvailabilityPerVehicle,
    decimal PreferredBonus,
    decimal InsufficientDataScore);

/// <summary>Optional hard exclusions based on performance. Off by default.</summary>
public sealed record EligibilityRestrictionsSetting(bool Enabled, decimal MinOtdPct, decimal MaxClaimsRatePct);

/// <summary>A configurable delay reason. <see cref="Attribution"/> is "Carrier", "NonCarrier" or "Unattributed".</summary>
public sealed record DelayReasonSetting(string Code, string Name, string Attribution);

/// <summary>Pickup and delivery tolerance, and the delay reasons that can be recorded against an event.</summary>
public sealed record DelayPolicySetting(int ToleranceMinutes, IReadOnlyList<DelayReasonSetting> Reasons);
