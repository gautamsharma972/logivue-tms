using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Application;

/// <summary>
/// Staff read and manage; a vendor-portal user (tied to a transporter, holding the execute permission) sees and runs only its own company's trips, and another company's trip is
/// reported as not found, never as forbidden. A driver is such a vendor-portal user.
/// </summary>
internal sealed class TrackingAccess(ICurrentUser user)
{
    public bool IsVendor => user.TransporterId is not null;

    public Guid? VendorTransporterId => user.TransporterId;

    public Guid? UserId => user.UserId;

    public Guid? TenantId => user.TenantId;

    private bool Has(string permission) => user.Permissions.Contains(permission);

    public bool CanRead => !IsVendor && (Has(TrackingPermissions.Read) || Has(TrackingPermissions.Manage));

    public bool CanManage => !IsVendor && Has(TrackingPermissions.Manage);

    public bool CanConfigure => !IsVendor && Has(TrackingPermissions.Configure);

    public bool CanManageGeofences => !IsVendor && Has(TrackingPermissions.Geofences);

    public bool CanManageLinks => !IsVendor && Has(TrackingPermissions.Links);

    /// <summary>Vendors with the execute permission, or staff acting on a carrier's behalf (a phone call from the driver).</summary>
    public bool CanExecute => IsVendor ? Has(TrackingPermissions.Execute) : Has(TrackingPermissions.Manage);

    public bool CanSee(TrackedShipment shipment) => CanSeeTransporter(shipment.TransporterId);

    public bool CanSeeTransporter(Guid? transporterId) =>
        IsVendor ? Has(TrackingPermissions.Execute) && transporterId == VendorTransporterId : CanRead;

    public static readonly Error Forbidden = Error.Forbidden("tracking.forbidden", "You are not allowed to do that.");

    public static readonly Error ShipmentNotFound = Error.NotFound("tracking.shipment_not_found", "Shipment not found.");

    public static readonly Error VehicleNotFound = Error.NotFound("tracking.vehicle_not_found", "Vehicle not found.");

    public static readonly Error AlertNotFound = Error.NotFound("tracking.alert_not_found", "Alert not found.");

    public static readonly Error ExceptionNotFound = Error.NotFound("tracking.exception_not_found", "Exception not found.");

    public static readonly Error GeofenceNotFound = Error.NotFound("geofences.not_found", "Geofence not found.");

    public static readonly Error LinkNotFound = Error.NotFound("tracking.link_not_found", "Tracking link not found.");
}

/// <summary>Every setting the tracking engine reads, loaded once so a batch of locations does not query for each one.</summary>
internal sealed record SettingsSnapshot(
    IntervalSetting Interval, HealthSetting Health, ValidationSetting Validation, GeofenceSetting Geofence, RouteSetting Route, DwellSetting Dwell, EtaSetting Eta, AlertSetting Alerts,
    RetentionSetting Retention, LinkSetting Links, MilestoneSetting Milestones);

internal interface ITrackingSettings
{
    Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task<SettingsSnapshot> SnapshotAsync(CancellationToken cancellationToken = default);
}

internal sealed class TrackingSettings(TrackingDbContext db) : ITrackingSettings
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        var json = await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.ValueJson).FirstOrDefaultAsync(cancellationToken);
        return Read<T>(key, json);
    }

    public async Task<SettingsSnapshot> SnapshotAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.ValueJson, cancellationToken);
        T Get<T>(string key) => Read<T>(key, rows.GetValueOrDefault(key));
        return new SettingsSnapshot(
            Get<IntervalSetting>(TrackingSettingKeys.Interval), Get<HealthSetting>(TrackingSettingKeys.Health), Get<ValidationSetting>(TrackingSettingKeys.Validation),
            Get<GeofenceSetting>(TrackingSettingKeys.Geofence), Get<RouteSetting>(TrackingSettingKeys.Route), Get<DwellSetting>(TrackingSettingKeys.Dwell), Get<EtaSetting>(TrackingSettingKeys.Eta),
            Get<AlertSetting>(TrackingSettingKeys.Alerts), Get<RetentionSetting>(TrackingSettingKeys.Retention), Get<LinkSetting>(TrackingSettingKeys.Links),
            Get<MilestoneSetting>(TrackingSettingKeys.Milestones));
    }

    private static T Read<T>(string key, string? json)
    {
        if (json is not null)
        {
            return JsonSerializer.Deserialize<T>(json, Json) ?? throw new InvalidOperationException($"Setting '{key}' could not be read as {typeof(T).Name}.");
        }

        return TrackingSettingDefaults.For(key) is T value ? value : throw new InvalidOperationException($"Setting '{key}' has no default of type {typeof(T).Name}.");
    }
}

internal static class Clock
{
    public static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    public static DateOnly TodayInIndia(this TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(India).DateTime);
}
