using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;

namespace Tms.Modules.Transporters.Application.Settings;

/// <summary>Business configuration (KPI weights, SLAs, thresholds). Services read it here, never as hard-coded policy values.</summary>
internal interface ITransporterSettings
{
    Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);
}

/// <summary>Well-known configuration keys. A tenant's own value (a row) wins; otherwise the default in <see cref="SettingDefaults"/> applies.</summary>
public static class SettingKeys
{
    public const string KpiWeights = "scorecard.weights";
    public const string ScorecardMinimumSample = "scorecard.minimumSampleSize";
    public const string PodSubmissionSlaHours = "pod.submissionSlaHours";
    public const string PlacementGraceMinutes = "placement.graceMinutes";
    public const string PlacementAlertMinutesBefore = "placement.alertMinutesBefore";
    public const string ExecutionDelayPolicy = "execution.delayPolicy";
    public const string PlannedTimes = "execution.plannedTimes";
    public const string RecommendationWeights = "recommendation.weights";
    public const string RecommendationScoring = "recommendation.scoring";
    public const string PerformanceWindowDays = "performance.windowDays";
    public const string EligibilityRestrictions = "eligibility.restrictions";
    public const string AlertsEnabled = "alerts.enabled";
    public const string DelayEscalation = "alerts.delayEscalationMinutes";
}

/// <summary>Weights are percentages and should sum to 100; they are renormalised over the KPIs that can be measured.</summary>
public sealed record KpiWeightsSetting(
    decimal OnTimePickup, decimal OnTimeDelivery, decimal PlacementCompliance, decimal TenderAcceptance, decimal PodCompliance, decimal ClaimsRate, decimal CostPerformance, decimal Availability);

public sealed record RecommendationWeightsSetting(
    decimal Rate, decimal OnTimePickup, decimal OnTimeDelivery, decimal PlacementCompliance, decimal PodCompliance, decimal TenderAcceptance, decimal ClaimsRate, decimal Availability);

/// <param name="ClaimsZeroAtPct">Claims rate at which the claims score reaches zero.</param>
/// <param name="AvailabilityPerVehicle">Availability score awarded per available vehicle (capped at 100).</param>
/// <param name="PreferredBonus">Points added for a preferred carrier or lane.</param>
/// <param name="InsufficientDataScore">Neutral score for a factor with too little data: it neither rewards nor penalises.</param>
public sealed record RecommendationScoringSetting(decimal ClaimsZeroAtPct, decimal AvailabilityPerVehicle, decimal PreferredBonus, decimal InsufficientDataScore);

/// <summary>Optional hard exclusions based on performance. Off by default.</summary>
public sealed record EligibilityRestrictionsSetting(bool Enabled, decimal MinOtdPct, decimal MaxClaimsRatePct);

public sealed record DelayReasonSetting(string Code, string Name, string Attribution);

public sealed record DelayPolicySetting(int ToleranceMinutes, IReadOnlyList<DelayReasonSetting> Reasons)
{
    public DelayReason? Find(string? code) =>
        Reasons.FirstOrDefault(r => string.Equals(r.Code, code?.Trim(), StringComparison.OrdinalIgnoreCase)) is { } r
            ? new DelayReason(r.Code, r.Name, r.Attribution switch { "Carrier" => DelayAttribution.Carrier, "NonCarrier" => DelayAttribution.NonCarrier, _ => DelayAttribution.Unattributed })
            : null;
}

/// <summary>The time of day (India) by which a pickup or delivery is due when only a date is known. Pickup is due on the planned pickup date; delivery by the deliver-by date.</summary>
public sealed record PlannedTimesSetting(string PickupDueTime, string DeliveryDueTime);

public sealed record DelayEscalationSetting(int CoordinatorMinutes, int ManagerAfterMinutes, int HeadAfterMinutes);

public static class SettingDefaults
{
    private static readonly Dictionary<string, object> Values = new()
    {
        [SettingKeys.KpiWeights] = new KpiWeightsSetting(10, 25, 15, 10, 5, 10, 15, 10),
        [SettingKeys.ScorecardMinimumSample] = 20,
        [SettingKeys.PodSubmissionSlaHours] = 24,
        [SettingKeys.PlacementGraceMinutes] = 15,
        [SettingKeys.PlacementAlertMinutesBefore] = 30,
        [SettingKeys.PlannedTimes] = new PlannedTimesSetting("20:00", "20:00"),
        [SettingKeys.ExecutionDelayPolicy] = new DelayPolicySetting(15,
        [
            new("TRANSPORTER_DELAY", "Transporter delay", "Carrier"),
            new("VEHICLE_BREAKDOWN", "Vehicle breakdown", "Carrier"),
            new("DOCUMENTATION_ISSUE", "Documentation issue", "Carrier"),
            new("CUSTOMER_DELAY", "Customer delay", "NonCarrier"),
            new("WAREHOUSE_DELAY", "Warehouse delay", "NonCarrier"),
            new("TRAFFIC", "Traffic", "NonCarrier"),
            new("ROUTE_RESTRICTION", "Route restriction", "NonCarrier"),
            new("WEATHER", "Weather", "NonCarrier"),
            new("FORCE_MAJEURE", "Force majeure", "NonCarrier"),
            new("OTHER", "Other", "Unattributed"),
        ]),
        [SettingKeys.RecommendationWeights] = new RecommendationWeightsSetting(30, 20, 15, 10, 5, 5, 5, 10),
        [SettingKeys.RecommendationScoring] = new RecommendationScoringSetting(5, 40, 5, 60),
        [SettingKeys.PerformanceWindowDays] = 90,
        [SettingKeys.EligibilityRestrictions] = new EligibilityRestrictionsSetting(false, 90, 3),
        [SettingKeys.AlertsEnabled] = true,
        [SettingKeys.DelayEscalation] = new DelayEscalationSetting(0, 60, 120),
    };

    public static IReadOnlyCollection<string> Keys => Values.Keys;

    public static object? For(string key) => Values.GetValueOrDefault(key);

    public static Type? TypeOf(string key) => Values.GetValueOrDefault(key)?.GetType();
}

internal sealed class TransporterSettings(TransportersDbContext db) : ITransporterSettings
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var json = await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.ValueJson).FirstOrDefaultAsync(cancellationToken);
        if (json is not null)
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException($"Setting '{key}' could not be read as {typeof(T).Name}.");
        }

        return SettingDefaults.For(key) is T value ? value : throw new InvalidOperationException($"Setting '{key}' has no default of type {typeof(T).Name}.");
    }
}
