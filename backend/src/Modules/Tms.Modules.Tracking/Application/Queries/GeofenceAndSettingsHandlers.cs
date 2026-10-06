using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Application.Queries;

internal sealed class GeofenceHandler(TrackingDbContext db, TrackingAccess access, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<GeofenceDto>>> ListAsync(GeofenceType? type, GeofenceStatus? status, CancellationToken cancellationToken)
    {
        if (!access.CanRead && !access.CanManageGeofences)
        {
            return TrackingAccess.Forbidden;
        }

        var rows = db.Geofences.AsNoTracking().AsQueryable();
        if (type is { } t)
        {
            rows = rows.Where(g => g.Type == t);
        }

        if (status is { } s)
        {
            rows = rows.Where(g => g.Status == s);
        }

        return (await rows.OrderBy(g => g.Code).ToListAsync(cancellationToken)).Select(TrackingMapper.Geofence).ToList();
    }

    public async Task<Result<GeofenceDto>> CreateAsync(SaveGeofenceRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageGeofences || user.TenantId is not { } tenantId)
        {
            return TrackingAccess.Forbidden;
        }

        var normalised = request.Code.Trim().ToUpperInvariant();
        if (await db.Geofences.AnyAsync(g => g.Code == normalised, cancellationToken))
        {
            return Error.Conflict("geofences.code_taken", "A geofence with that code already exists.");
        }

        var created = Geofence.Create(tenantId, request.Code, request.Name, request.Type, request.CenterLatitude, request.CenterLongitude, request.RadiusMeters, Ring(request.Polygon), request.EffectiveFrom, request.EffectiveTo);
        if (created.IsFailure)
        {
            return created.Error;
        }

        db.Geofences.Add(created.Value);
        await db.SaveChangesAsync(cancellationToken);
        return TrackingMapper.Geofence(created.Value);
    }

    public async Task<Result<GeofenceDto>> UpdateAsync(Guid id, SaveGeofenceRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManageGeofences)
        {
            return TrackingAccess.Forbidden;
        }

        var geofence = await db.Geofences.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (geofence is null)
        {
            return TrackingAccess.GeofenceNotFound;
        }

        if (request.Version is { } version)
        {
            db.Entry(geofence).Property(g => g.Version).OriginalValue = version;
        }

        var code = request.Code.Trim().ToUpperInvariant();
        if (await db.Geofences.AnyAsync(g => g.Code == code && g.Id != id, cancellationToken))
        {
            return Error.Conflict("geofences.code_taken", "A geofence with that code already exists.");
        }

        var applied = geofence.Apply(request.Code, request.Name, request.Type, request.CenterLatitude, request.CenterLongitude, request.RadiusMeters, Ring(request.Polygon), request.EffectiveFrom, request.EffectiveTo,
            request.Status ?? geofence.Status);
        if (applied.IsFailure)
        {
            return applied.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        return TrackingMapper.Geofence(geofence);
    }

    /// <summary>A geofence that trips have already used is switched off, not deleted, so the events that refer to it keep their meaning.</summary>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!access.CanManageGeofences)
        {
            return TrackingAccess.Forbidden;
        }

        var geofence = await db.Geofences.FirstOrDefaultAsync(g => g.Id == id, cancellationToken);
        if (geofence is null)
        {
            return TrackingAccess.GeofenceNotFound;
        }

        if (await db.GeofenceEvents.AnyAsync(e => e.GeofenceId == id, cancellationToken) || await db.Presences.AnyAsync(p => p.SubjectId == id, cancellationToken))
        {
            geofence.Apply(geofence.Code, geofence.Name, geofence.Type, geofence.CenterLatitude, geofence.CenterLongitude, geofence.RadiusMeters, geofence.Polygon, geofence.EffectiveFrom, geofence.EffectiveTo, GeofenceStatus.Inactive);
        }
        else
        {
            db.Geofences.Remove(geofence);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    private static List<GeoPoint>? Ring(IReadOnlyList<double[]>? polygon) =>
        polygon is null ? null : polygon.Where(p => p.Length >= 2).Select(p => new GeoPoint(p[0], p[1])).ToList();
}

public sealed record SettingDto(string Key, JsonElement Value, bool IsCustomised);

/// <summary>The tracking thresholds a tenant can change: how often to report, when tracking is stale, how far off route, how long a stop may last, how late is late.</summary>
internal sealed class SettingsHandler(TrackingDbContext db, TrackingAccess access, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<SettingDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!access.CanRead && !access.CanExecute)
        {
            return TrackingAccess.Forbidden;
        }

        var rows = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.ValueJson, cancellationToken);
        return TrackingSettingDefaults.Keys.Order().Select(key =>
            rows.TryGetValue(key, out var json)
                ? new SettingDto(key, JsonDocument.Parse(json).RootElement.Clone(), true)
                : new SettingDto(key, JsonSerializer.SerializeToElement(TrackingSettingDefaults.For(key), TrackingSettings.Json), false)).ToList();
    }

    public async Task<Result<SettingDto>> SaveAsync(string key, JsonElement value, CancellationToken cancellationToken)
    {
        if (!access.CanConfigure || user.TenantId is not { } tenantId)
        {
            return TrackingAccess.Forbidden;
        }

        if (TrackingSettingDefaults.TypeOf(key) is not { } type)
        {
            return Error.NotFound("settings.unknown", "There is no such setting.");
        }

        object? parsed;
        try
        {
            parsed = value.Deserialize(type, TrackingSettings.Json);
        }
        catch (JsonException)
        {
            return Error.Validation("settings.invalid", $"The value is not valid for '{key}'.");
        }

        if (parsed is null || Validate(parsed) is { } problem)
        {
            return Error.Validation("settings.invalid", parsed is null ? $"The value is not valid for '{key}'." : Validate(parsed)!);
        }

        var json = JsonSerializer.Serialize(parsed, TrackingSettings.Json);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (row is null)
        {
            db.Settings.Add(TrackingSetting.Create(tenantId, key, json));
        }
        else
        {
            row.Change(json);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SettingDto(key, JsonDocument.Parse(json).RootElement.Clone(), true);
    }

    internal static string? Validate(object value) => value switch
    {
        IntervalSetting i when i.ActiveSeconds is < 10 or > 3600 || i.StationarySeconds is < 10 or > 7200 || i.ApproachingSeconds is < 10 or > 3600 || i.ApproachingKm is < 0 or > 200 => "Intervals must be between 10 seconds and 1 hour (2 hours when stationary).",
        HealthSetting h when h.StaleAfterMinutes < 1 || h.LostAfterMinutes <= h.StaleAfterMinutes => "Tracking is lost only after it is stale: give a longer time for lost than for stale.",
        ValidationSetting v when v.MaxAccuracyM < 1 || v.RejectAccuracyM < v.MaxAccuracyM || v.MaxSpeedKph < 20 || v.MaxImpliedSpeedKph < v.MaxSpeedKph || v.FutureToleranceMinutes < 0 || v.RejectOlderThanDays < 1 => "The location limits do not make sense together.",
        GeofenceSetting g when g.DefaultRadiusM is < 20 or > 50_000 || g.MinConfirmationPoints < 1 || g.EntryConfirmationSeconds < 0 || g.ExitConfirmationSeconds < 0 => "Geofence settings are out of range.",
        RouteSetting r when r.DeviationKm <= 0 || r.DeviationMinutes < 0 || r.HighKm < r.DeviationKm || r.CriticalKm < r.HighKm => "The route corridor and its severity limits must increase.",
        DwellSetting d when d.ExcessThresholdMinutes < 0 || d.StationaryRadiusM < 10 || d.MinStationaryMinutes < 1 || d.UnplannedStopMinutes < d.MinStationaryMinutes || d.ExpectedMinutes?.Values.Any(v => v < 0) == true => "Dwell settings are out of range.",
        EtaSetting e when e.AverageSpeedKph is < 10 or > 120 || e.SpeedBlend is < 0 or > 1 || e.OnTimeToleranceMinutes < 0 || e.DelayedAfterMinutes < e.OnTimeToleranceMinutes || e.SeverelyDelayedAfterMinutes < e.DelayedAfterMinutes => "Arrival settings are out of range, or the lateness steps do not increase.",
        AlertSetting a when a.Escalation?.Any(s => s.AfterMinutes < 1 || string.IsNullOrWhiteSpace(s.Level)) == true || a.Escalation?.Select(s => s.AfterMinutes).SequenceEqual(a.Escalation.Select(s => s.AfterMinutes).Order()) == false => "Each escalation step needs a level and the steps must get later.",
        RetentionSetting r when r.RawLocationDays < 1 || r.AggregatedRouteDays < r.RawLocationDays => "Keep the summary at least as long as the raw locations.",
        LinkSetting l when l.DefaultValidityDays < 1 || l.MaxValidityDays < l.DefaultValidityDays => "Link validity must be at least a day, and the maximum no shorter than the default.",
        MilestoneSetting m when m.Enabled?.Any(x => !Enum.TryParse<MilestoneType>(x, true, out _)) == true => "One of the milestones is not known.",
        _ => null,
    };
}
