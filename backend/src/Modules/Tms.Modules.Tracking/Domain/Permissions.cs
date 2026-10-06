using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Domain;

public static class TrackingPermissions
{
    public const string Read = "tracking.read";

    /// <summary>Acknowledge alerts, work exceptions, record manual milestones and ETA overrides.</summary>
    public const string Manage = "tracking.manage";

    /// <summary>Vendor portal / drivers: start and stop tracking on the trips of your own company and send locations.</summary>
    public const string Execute = "tracking.execute";

    public const string Configure = "tracking.configure";

    /// <summary>Geofences and customer tracking links.</summary>
    public const string Geofences = "tracking.geofences.manage";

    public const string Links = "tracking.links.manage";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "Tracking", "View the control tower, shipment and vehicle tracking, alerts and exceptions"),
        new(Manage, "Tracking", "Acknowledge alerts, work tracking exceptions, correct milestones and ETAs"),
        new(Execute, "Tracking", "Vendor portal: start and stop tracking on own trips and send locations", ExternalAllowed: true),
        new(Configure, "Tracking", "Change tracking intervals, thresholds and alert rules"),
        new(Geofences, "Tracking", "Create and change geofences"),
        new(Links, "Tracking", "Create and revoke customer tracking links"),
    ];
}
