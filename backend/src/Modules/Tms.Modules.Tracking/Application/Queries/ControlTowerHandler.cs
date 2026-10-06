using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Domain;
using Tms.Modules.Tracking.Infrastructure.Persistence;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Tracking.Application.Queries;

/// <summary>The control tower's figures, read from the read models so opening it does not touch the raw GPS history.</summary>
internal sealed class ControlTowerHandler(TrackingDbContext db, TrackingAccess access, TrackingHealthMonitor monitor, ShipmentQueryHandler shipments, TimeProvider clock)
{
    private bool Allowed => access.IsVendor ? access.CanExecute : access.CanRead;

    public async Task<Result<ControlTowerSummaryDto>> SummaryAsync(Guid? transporterId, CancellationToken cancellationToken)
    {
        if (!Allowed)
        {
            return TrackingAccess.Forbidden;
        }

        await monitor.EvaluateAsync(cancellationToken);
        var now = clock.GetUtcNow();
        var rows = db.Shipments.AsNoTracking().AsQueryable();
        Guid? scope = access.IsVendor ? access.VendorTransporterId : transporterId;
        if (scope is { } t)
        {
            rows = rows.Where(s => s.TransporterId == t);
        }

        var active = rows.Where(s => s.Execution != ExecutionStatus.Completed && s.Execution != ExecutionStatus.Cancelled && s.Execution != ExecutionStatus.Delivered);
        var tracked = active.Where(s => s.StartedAt != null);
        var today = new DateTimeOffset(clock.TodayInIndia().ToDateTime(TimeOnly.MinValue), Clock.India);

        var counts = await tracked.GroupBy(s => 1).Select(g => new
        {
            Active = g.Count(),
            OnTime = g.Count(s => s.Risk == RiskStatus.OnTime),
            AtRisk = g.Count(s => s.Risk == RiskStatus.AtRisk),
            Delayed = g.Count(s => s.Risk == RiskStatus.Delayed || s.Risk == RiskStatus.SeverelyDelayed),
            Stale = g.Count(s => s.Tracking == TrackingHealth.Stale),
            Lost = g.Count(s => s.Tracking == TrackingHealth.Lost),
            Deviations = g.Count(s => s.DeviationOpen),
        }).FirstOrDefaultAsync(cancellationToken);

        var ids = rows.Select(s => s.Id);
        var openStates = new[] { ExceptionStatus.Open, ExceptionStatus.Acknowledged, ExceptionStatus.InProgress, ExceptionStatus.Escalated };
        var exceptions = await db.Exceptions.AsNoTracking().CountAsync(e => ids.Contains(e.TrackedShipmentId) && openStates.Contains(e.Status), cancellationToken);
        var alerts = await db.Alerts.AsNoTracking().CountAsync(a => ids.Contains(a.TrackedShipmentId) && a.Status != AlertStatus.Resolved, cancellationToken);
        var excess = await db.Alerts.AsNoTracking().CountAsync(a => ids.Contains(a.TrackedShipmentId) && a.Type == AlertType.ExcessiveDwell && a.Status != AlertStatus.Resolved, cancellationToken);
        var completed = await rows.CountAsync(s => s.CompletedAt != null && s.CompletedAt >= today, cancellationToken);
        var notStarted = await active.CountAsync(s => s.StartedAt == null, cancellationToken);

        return new ControlTowerSummaryDto(
            counts?.Active ?? 0, counts?.OnTime ?? 0, counts?.AtRisk ?? 0, counts?.Delayed ?? 0, counts?.Stale ?? 0, counts?.Lost ?? 0, counts?.Deviations ?? 0, excess, exceptions, completed, notStarted, alerts, now);
    }

    public Task<Result<PagedResult<TrackedShipmentSummaryDto>>> ShipmentsAsync(ListShipmentsQuery query, CancellationToken cancellationToken) => shipments.ListAsync(query, cancellationToken);

    /// <summary>Everything the map needs in one call: every vehicle that is on a trip, and where it is.</summary>
    public async Task<Result<IReadOnlyList<TrackedShipmentSummaryDto>>> MapAsync(CancellationToken cancellationToken)
    {
        var all = await shipments.ListAsync(new ListShipmentsQuery(ActiveOnly: true, PageSize: 500), cancellationToken);
        return all.IsFailure ? all.Error : all.Value.Items.Where(s => s.Latitude is not null).ToList();
    }
}

internal sealed class VehicleQueryHandler(TrackingDbContext db, TrackingAccess access, TrackingHealthMonitor monitor, TimeProvider clock)
{
    private bool Allowed => access.IsVendor ? access.CanExecute : access.CanRead;

    public async Task<Result<PagedResult<VehicleTrackingDto>>> ListAsync(string? search, TrackingHealth? health, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!Allowed)
        {
            return TrackingAccess.Forbidden;
        }

        await monitor.EvaluateAsync(cancellationToken);
        var rows = db.Positions.AsNoTracking().AsQueryable();
        if (access.IsVendor)
        {
            rows = rows.Where(p => p.TransporterId == access.VendorTransporterId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            rows = rows.Where(p => p.VehicleReference.Contains(term) || (p.DriverName != null && p.DriverName.Contains(term)) || (p.ShipmentReference != null && p.ShipmentReference.Contains(term)));
        }

        if (health is { } h)
        {
            rows = rows.Where(p => p.Health == h);
        }

        var paged = await rows.OrderBy(p => p.VehicleReference).ToPagedAsync(page, pageSize, cancellationToken);
        var items = paged.Items.ToList();
        var shipmentIds = items.Where(p => p.ShipmentId != null).Select(p => p.ShipmentId!.Value).ToList();
        var shipmentRows = await db.Shipments.AsNoTracking().Where(s => shipmentIds.Contains(s.ShipmentId)).ToDictionaryAsync(s => s.ShipmentId, cancellationToken);
        var now = clock.GetUtcNow();
        var day = new DateTimeOffset(clock.TodayInIndia().ToDateTime(TimeOnly.MinValue), Clock.India);
        var dtos = new List<VehicleTrackingDto>();
        foreach (var p in items)
        {
            shipmentRows.TryGetValue(p.ShipmentId ?? Guid.Empty, out var s);
            dtos.Add(new VehicleTrackingDto(ToPosition(p, now), s?.ShipmentId, s?.ShipmentReference, s?.TripReference, s?.Execution, s?.Risk, s?.CurrentEtaAt, s?.OriginName, s?.DestinationName, await TodayKmAsync(p.VehicleReference, day, cancellationToken), s?.DriverPhone));
        }

        return new PagedResult<VehicleTrackingDto>(dtos, paged.Page, paged.PageSize, paged.TotalCount);
    }

    public async Task<Result<VehicleTrackingDto>> CurrentAsync(string vehicle, CancellationToken cancellationToken)
    {
        if (!Allowed)
        {
            return TrackingAccess.Forbidden;
        }

        var position = await db.Positions.AsNoTracking().FirstOrDefaultAsync(p => p.VehicleReference == vehicle, cancellationToken);
        if (position is null || access.IsVendor && position.TransporterId != access.VendorTransporterId)
        {
            return TrackingAccess.VehicleNotFound;
        }

        var shipment = position.ShipmentId is { } id ? await db.Shipments.AsNoTracking().FirstOrDefaultAsync(s => s.ShipmentId == id, cancellationToken) : null;
        var day = new DateTimeOffset(clock.TodayInIndia().ToDateTime(TimeOnly.MinValue), Clock.India);
        return new VehicleTrackingDto(ToPosition(position, clock.GetUtcNow()), shipment?.ShipmentId, shipment?.ShipmentReference, shipment?.TripReference, shipment?.Execution, shipment?.Risk, shipment?.CurrentEtaAt,
            shipment?.OriginName, shipment?.DestinationName, await TodayKmAsync(vehicle, day, cancellationToken), shipment?.DriverPhone);
    }

    /// <summary>Where a vehicle went on a day, thinned for drawing.</summary>
    public async Task<Result<IReadOnlyList<LocationDto>>> HistoryAsync(string vehicle, DateOnly? day, int? maxPoints, CancellationToken cancellationToken)
    {
        if (!Allowed)
        {
            return TrackingAccess.Forbidden;
        }

        var position = await db.Positions.AsNoTracking().FirstOrDefaultAsync(p => p.VehicleReference == vehicle, cancellationToken);
        if (access.IsVendor && position?.TransporterId != access.VendorTransporterId)
        {
            return TrackingAccess.VehicleNotFound;
        }

        var date = day ?? clock.TodayInIndia();
        var from = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), Clock.India);
        var rows = await db.Locations.AsNoTracking().Where(l => l.VehicleReference == vehicle && l.Validation == LocationValidation.Valid && l.CapturedAt >= from && l.CapturedAt < from.AddDays(1))
            .OrderBy(l => l.CapturedAt).Select(l => new LocationDto(l.Id, l.Latitude, l.Longitude, l.AccuracyMeters, l.SpeedKph, l.Heading, l.CapturedAt, l.ReceivedAt, l.Validation, l.Anomalies, l.Reasons, l.IsLate, l.DeviceId, l.Source))
            .Take(100_000).ToListAsync(cancellationToken);
        var max = maxPoints is > 1 ? maxPoints.Value : 2000;
        return rows.Count <= max ? rows : Enumerable.Range(0, max).Select(i => rows[(int)Math.Round(i * (rows.Count - 1.0) / (max - 1))]).ToList();
    }

    private static CurrentLocationDto ToPosition(CurrentVehiclePosition p, DateTimeOffset now) => new(
        p.VehicleReference, p.TripReference, p.ShipmentReference, p.Latitude, p.Longitude, p.AccuracyMeters, p.SpeedKph, p.Heading, p.LastCapturedAt, p.LastReceivedAt, p.Health, p.Moving,
        (int)Math.Max(0, (now - p.LastCapturedAt).TotalMinutes), p.BatteryPercentage, p.NetworkType, p.DriverName, p.TransporterReference);

    /// <summary>The distance travelled today, from the trails of the vehicle's trips that ran today.</summary>
    private async Task<double> TodayKmAsync(string vehicle, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var rows = await db.Locations.AsNoTracking().Where(l => l.VehicleReference == vehicle && l.Validation == LocationValidation.Valid && !l.IsLate && l.CapturedAt >= since)
            .OrderBy(l => l.CapturedAt).Select(l => new { l.Latitude, l.Longitude, l.ShipmentId }).Take(20_000).ToListAsync(cancellationToken);
        double km = 0;
        for (var i = 1; i < rows.Count; i++)
        {
            if (rows[i].ShipmentId == rows[i - 1].ShipmentId)
            {
                var d = Geo.DistanceKm(new GeoPoint(rows[i - 1].Latitude, rows[i - 1].Longitude), new GeoPoint(rows[i].Latitude, rows[i].Longitude));
                km += d < 0.01 ? 0 : d;
            }
        }

        return Math.Round(km, 1);
    }
}
