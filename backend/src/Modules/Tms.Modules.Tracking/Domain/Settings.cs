namespace Tms.Modules.Tracking.Domain;

public static class TrackingSettingKeys
{
    public const string Interval = "tracking.interval";
    public const string Health = "tracking.health";
    public const string Validation = "tracking.validation";
    public const string Geofence = "tracking.geofence";
    public const string Route = "tracking.route";
    public const string Dwell = "tracking.dwell";
    public const string Eta = "tracking.eta";
    public const string Alerts = "tracking.alerts";
    public const string Retention = "tracking.retention";
    public const string Links = "tracking.links";
    public const string Milestones = "tracking.milestones";
    public const string Compliance = "tracking.compliance";
}

/// <param name="ActiveSeconds">How often a moving vehicle reports. A few minutes is enough for a truck and spares the driver's battery.</param>
/// <param name="StationarySeconds">When it has not moved for a while it reports less often.</param>
/// <param name="ApproachingSeconds">Near a stop, more often, so arrival is seen promptly.</param>
public sealed record IntervalSetting(int ActiveSeconds = 180, int StationarySeconds = 600, int ApproachingSeconds = 60, double ApproachingKm = 10, bool Adaptive = true);

/// <param name="StaleAfterMinutes">No location for this long: tracking is stale (the vehicle may still be moving).</param>
/// <param name="LostAfterMinutes">No location for this long: tracking is lost.</param>
public sealed record HealthSetting(int StaleAfterMinutes = 10, int LostAfterMinutes = 30, double MovingSpeedKph = 5);

/// <param name="MaxAccuracyM">A fix vaguer than this is kept but marked suspicious.</param>
/// <param name="RejectAccuracyM">A fix vaguer than this is refused.</param>
/// <param name="MaxSpeedKph">A reported speed above this is implausible for a truck.</param>
/// <param name="MaxImpliedSpeedKph">Speed implied by two consecutive fixes; above it the second is a jump, not a movement.</param>
/// <param name="RepeatedIdenticalLimit">The same coordinates this many times in a row while the device claims to move is a frozen or faked fix.</param>
/// <param name="ClockSkewMinutes">The device clock differing from the server's by more than this is noted.</param>
public sealed record ValidationSetting(
    int MaxAccuracyM = 100, int RejectAccuracyM = 2000, int FutureToleranceMinutes = 5, int RejectOlderThanDays = 7, double MaxSpeedKph = 140, double MaxImpliedSpeedKph = 180,
    int RepeatedIdenticalLimit = 20, int ClockSkewMinutes = 10, bool TreatMockLocationAsSuspicious = true);

/// <param name="EntryConfirmationSeconds">A vehicle must stay inside this long, over at least <paramref name="MinConfirmationPoints"/> good fixes, before it has "entered".</param>
public sealed record GeofenceSetting(
    int DefaultRadiusM = 200, int MaxAccuracyM = 100, int EntryConfirmationSeconds = 60, int ExitConfirmationSeconds = 60, int MinConfirmationPoints = 2, int StayedAfterMinutes = 30,
    double ApproachingKm = 10);

/// <param name="DeviationKm">Farther than this from the planned route is off the corridor.</param>
/// <param name="DeviationMinutes">...for this long, before it counts as a deviation (a service road or a queue at a toll is not).</param>
/// <param name="ResolveMinutes">Back on the corridor this long before the deviation is closed.</param>
public sealed record RouteSetting(double DeviationKm = 3, int DeviationMinutes = 10, int ResolveMinutes = 5, double HighKm = 3.5, double CriticalKm = 10, int CriticalMinutes = 90, int MinOffRoutePoints = 2);

/// <param name="ExpectedMinutes">Expected stay by kind of place; "Default" applies to any other.</param>
/// <param name="ExcessThresholdMinutes">Longer than expected by this much is excessive.</param>
/// <param name="StationarySpeedKph">Slower than this is stationary.</param>
/// <param name="StationaryRadiusM">Within this of where it stopped is still the same stop.</param>
/// <param name="MinStationaryMinutes">Stopped this long before it is recorded as a stop at all.</param>
/// <param name="UnplannedStopMinutes">Stopped outside any known place this long: an unplanned stop.</param>
public sealed record DwellSetting(
    Dictionary<string, int>? ExpectedMinutes = null, int ExcessThresholdMinutes = 15, double StationarySpeedKph = 3, int StationaryRadiusM = 100, int MinStationaryMinutes = 10, int UnplannedStopMinutes = 30)
{
    public int Expected(string kind)
    {
        var map = ExpectedMinutes ?? DefaultExpected;
        return map.TryGetValue(kind, out var minutes) ? minutes : map.GetValueOrDefault("Default", 30);
    }

    public static Dictionary<string, int> DefaultExpected => new(StringComparer.OrdinalIgnoreCase) { ["Warehouse"] = 60, ["Origin"] = 60, ["Customer"] = 30, ["Destination"] = 30, ["Hub"] = 45, ["CrossDock"] = 45, ["Depot"] = 45, ["Default"] = 30 };
}

/// <param name="AverageSpeedKph">Used when there is no recent moving speed to go on.</param>
/// <param name="SpeedBlend">How much the speed actually observed counts against the average (0 = ignore it, 1 = use only it).</param>
/// <param name="OnTimeToleranceMinutes">Arriving this much after plan is still on time.</param>
/// <param name="DelayedAfterMinutes">Later than this is delayed (high risk).</param>
/// <param name="SeverelyDelayedAfterMinutes">Later than this is severely delayed (critical).</param>
/// <param name="SlaBufferMinutes">Extra minutes allowed after the delivery window before it is a miss.</param>
/// <param name="PublishChangeMinutes">An estimate that moved by this much is announced to other modules; smaller changes are not.</param>
/// <param name="MinChangeMinutes">A new estimate is kept in the history only if it moved by this much, or it is older than <paramref name="RecordEveryMinutes"/>.</param>
public sealed record EtaSetting(
    double AverageSpeedKph = 45, double SpeedBlend = 0.3, int OnTimeToleranceMinutes = 10, int DelayedAfterMinutes = 30, int SeverelyDelayedAfterMinutes = 60, int SlaBufferMinutes = 0,
    int MinChangeMinutes = 3, int RecordEveryMinutes = 15, int PublishChangeMinutes = 15);

public sealed record AlertRule(Severity Severity, bool CreatesException, int DueMinutes);

/// <param name="AfterMinutes">Minutes an exception may sit without being resolved before it moves up to this level.</param>
public sealed record EscalationStep(int AfterMinutes, string Level);

public sealed record AlertSetting(Dictionary<string, AlertRule>? Rules = null, List<EscalationStep>? Escalation = null)
{
    public AlertRule RuleFor(AlertType type)
    {
        var rules = Rules ?? DefaultRules;
        return rules.TryGetValue(type.ToString(), out var rule) ? rule : DefaultRules[type.ToString()];
    }

    public IReadOnlyList<EscalationStep> Steps => Escalation ?? DefaultEscalation;

    public static Dictionary<string, AlertRule> DefaultRules => new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(AlertType.TrackingStale)] = new(Severity.Warning, false, 60),
        [nameof(AlertType.TrackingLost)] = new(Severity.High, true, 60),
        [nameof(AlertType.RouteDeviation)] = new(Severity.High, true, 120),
        [nameof(AlertType.ExcessiveDwell)] = new(Severity.Warning, false, 120),
        [nameof(AlertType.UnplannedStop)] = new(Severity.Warning, false, 120),
        [nameof(AlertType.EtaAtRisk)] = new(Severity.Warning, false, 120),
        [nameof(AlertType.EtaDelayed)] = new(Severity.High, true, 90),
        [nameof(AlertType.DeliverySlaRisk)] = new(Severity.Critical, true, 60),
        [nameof(AlertType.GeofenceException)] = new(Severity.Warning, false, 120),
        [nameof(AlertType.GpsAnomaly)] = new(Severity.Warning, false, 240),
        [nameof(AlertType.VehicleStationary)] = new(Severity.Informational, false, 240),
        [nameof(AlertType.GpsUnavailable)] = new(Severity.Warning, false, 60),
    };

    public static List<EscalationStep> DefaultEscalation => [new(30, "Transport manager"), new(120, "Operations head")];
}

public sealed record RetentionSetting(int RawLocationDays = 90, int AggregatedRouteDays = 365);

public sealed record LinkSetting(int DefaultValidityDays = 14, int MaxValidityDays = 90);

/// <summary>Which milestones this tenant's trips go through. Not every trip needs every one, so the rest are simply not created.</summary>
public sealed record MilestoneSetting(List<string>? Enabled = null)
{
    public static List<string> DefaultEnabled => [nameof(MilestoneType.VehicleAssigned), nameof(MilestoneType.ArrivedOrigin), nameof(MilestoneType.DepartedOrigin), nameof(MilestoneType.InTransit),
        nameof(MilestoneType.ApproachingStop), nameof(MilestoneType.ArrivedStop), nameof(MilestoneType.DepartedStop), nameof(MilestoneType.ApproachingDestination), nameof(MilestoneType.ArrivedDestination),
        nameof(MilestoneType.Delivered), nameof(MilestoneType.TrackingCompleted)];

    public bool Has(MilestoneType type) => (Enabled ?? DefaultEnabled).Contains(type.ToString(), StringComparer.OrdinalIgnoreCase);
}

public static class TrackingSettingDefaults
{
    private static readonly Dictionary<string, (Type Type, Func<object> Make)> Entries = new()
    {
        [TrackingSettingKeys.Interval] = (typeof(IntervalSetting), () => new IntervalSetting()),
        [TrackingSettingKeys.Health] = (typeof(HealthSetting), () => new HealthSetting()),
        [TrackingSettingKeys.Validation] = (typeof(ValidationSetting), () => new ValidationSetting()),
        [TrackingSettingKeys.Geofence] = (typeof(GeofenceSetting), () => new GeofenceSetting()),
        [TrackingSettingKeys.Route] = (typeof(RouteSetting), () => new RouteSetting()),
        [TrackingSettingKeys.Dwell] = (typeof(DwellSetting), () => new DwellSetting(DwellSetting.DefaultExpected)),
        [TrackingSettingKeys.Eta] = (typeof(EtaSetting), () => new EtaSetting()),
        [TrackingSettingKeys.Alerts] = (typeof(AlertSetting), () => new AlertSetting(AlertSetting.DefaultRules, AlertSetting.DefaultEscalation)),
        [TrackingSettingKeys.Retention] = (typeof(RetentionSetting), () => new RetentionSetting()),
        [TrackingSettingKeys.Links] = (typeof(LinkSetting), () => new LinkSetting()),
        [TrackingSettingKeys.Compliance] = (typeof(ComplianceSetting), () => new ComplianceSetting()),
        [TrackingSettingKeys.Milestones] = (typeof(MilestoneSetting), () => new MilestoneSetting(MilestoneSetting.DefaultEnabled)),
    };

    public static IReadOnlyCollection<string> Keys => Entries.Keys;

    public static object? For(string key) => Entries.TryGetValue(key, out var entry) ? entry.Make() : null;

    public static Type? TypeOf(string key) => Entries.TryGetValue(key, out var entry) ? entry.Type : null;
}
