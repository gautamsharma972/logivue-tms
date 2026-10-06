using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Tracking.Domain;

namespace Tms.Modules.Tracking.Infrastructure.Persistence.Configurations;

internal sealed class TrackedShipmentConfiguration : IEntityTypeConfiguration<TrackedShipment>
{
    public void Configure(EntityTypeBuilder<TrackedShipment> b)
    {
        b.ToTable("shipments");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(s => s.TripReference).HasMaxLength(40).IsRequired();
        b.Property(s => s.TransporterReference).HasMaxLength(200);
        b.Property(s => s.VehicleReference).HasMaxLength(20);
        b.Property(s => s.DriverName).HasMaxLength(150);
        b.Property(s => s.DriverPhone).HasMaxLength(20);
        b.Property(s => s.CustomerName).HasMaxLength(200);
        b.Property(s => s.OriginName).HasMaxLength(200);
        b.Property(s => s.DestinationName).HasMaxLength(200);
        b.Property(s => s.RouteSource).HasMaxLength(12);
        b.Property(s => s.Execution).HasConversion<string>().HasMaxLength(24);
        b.Property(s => s.Tracking).HasConversion<string>().HasMaxLength(12);
        b.Property(s => s.Risk).HasConversion<string>().HasMaxLength(16);
        b.Property(s => s.Delivery).HasConversion<string>().HasMaxLength(16);
        b.Property(s => s.PlannedDistanceKm).HasPrecision(10, 2);
        b.Property(s => s.EtaOverrideReason).HasMaxLength(500);
        b.Property(s => s.DelayReason).HasConversion<string?>().HasMaxLength(20);
        b.Property(s => s.DelayNote).HasMaxLength(500);
        b.Property(s => s.DwellWhere).HasMaxLength(30);
        b.Property(s => s.ActualRouteJson).HasColumnType("longtext");
        b.Ignore(s => s.CurrentEtaAt);
        b.Ignore(s => s.IsActive);
        b.HasMany(s => s.Stops).WithOne().HasForeignKey(x => x.TrackedShipmentId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(s => s.Stops).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(s => new { s.TenantId, s.ShipmentId }).IsUnique();
        b.HasIndex(s => new { s.TenantId, s.TripReference });
        b.HasIndex(s => new { s.TenantId, s.VehicleReference });
        b.HasIndex(s => new { s.TenantId, s.TransporterId });
        b.HasIndex(s => new { s.TenantId, s.Execution, s.Tracking, s.Risk });
    }
}

internal sealed class ShipmentStopConfiguration : IEntityTypeConfiguration<ShipmentStop>
{
    public void Configure(EntityTypeBuilder<ShipmentStop> b)
    {
        b.ToTable("shipment_stops");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Name).HasMaxLength(200).IsRequired();
        b.Property(s => s.City).HasMaxLength(100);
        b.Property(s => s.Reference).HasMaxLength(64);
        b.Property(s => s.CustomerReference).HasMaxLength(64);
        b.Property(s => s.CustomerName).HasMaxLength(200);
        b.Property(s => s.Kind).HasConversion<string>().HasMaxLength(8);
        b.Property(s => s.PlaceType).HasConversion<string>().HasMaxLength(16);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(s => s.Risk).HasConversion<string>().HasMaxLength(16);
        b.Ignore(s => s.HasLocation);
        b.Ignore(s => s.Point);
        b.Ignore(s => s.PlaceKindLabel);
        b.HasIndex(s => new { s.TenantId, s.TrackedShipmentId, s.Sequence });
    }
}

internal sealed class TrackingSessionConfiguration : IEntityTypeConfiguration<TrackingSession>
{
    public void Configure(EntityTypeBuilder<TrackingSession> b)
    {
        b.ToTable("tracking_sessions");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Reference).HasMaxLength(20).IsRequired();
        b.Property(s => s.TripReference).HasMaxLength(40).IsRequired();
        b.Property(s => s.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(s => s.VehicleReference).HasMaxLength(20);
        b.Property(s => s.DriverReference).HasMaxLength(150);
        b.Property(s => s.DeviceId).HasMaxLength(100);
        b.Property(s => s.StopReason).HasMaxLength(200);
        b.Property(s => s.Status).HasConversion<string>().HasMaxLength(12);
        b.Ignore(s => s.IsOpen);
        b.HasIndex(s => new { s.TenantId, s.Reference }).IsUnique();
        b.HasIndex(s => new { s.TenantId, s.TrackedShipmentId, s.Status });
        b.HasIndex(s => new { s.TenantId, s.TransporterId, s.Status });
        b.HasIndex(s => new { s.TenantId, s.VehicleReference, s.Status });
    }
}

internal sealed class TrackingLocationConfiguration : IEntityTypeConfiguration<TrackingLocation>
{
    public void Configure(EntityTypeBuilder<TrackingLocation> b)
    {
        b.ToTable("tracking_locations");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.ClientLocationReference).HasMaxLength(64).IsRequired();
        b.Property(l => l.TripReference).HasMaxLength(40).IsRequired();
        b.Property(l => l.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(l => l.VehicleReference).HasMaxLength(20);
        b.Property(l => l.DriverReference).HasMaxLength(150);
        b.Property(l => l.DeviceId).HasMaxLength(100).IsRequired();
        b.Property(l => l.Source).HasConversion<string>().HasMaxLength(16);
        b.Property(l => l.Validation).HasConversion<string>().HasMaxLength(12);
        b.Property(l => l.Anomalies).HasConversion<int>();
        b.Property(l => l.Reasons).HasMaxLength(600);
        b.Property(l => l.AppVersion).HasMaxLength(30);
        b.Property(l => l.NetworkType).HasMaxLength(20);
        // The same fix retried from the same device on the same trip is one row: this is what makes a batch safe to send twice.
        b.HasIndex(l => new { l.TenantId, l.DeviceId, l.TripReference, l.ClientLocationReference }).IsUnique();
        b.HasIndex(l => new { l.TenantId, l.TripReference, l.CapturedAt });
        b.HasIndex(l => new { l.TenantId, l.VehicleReference, l.CapturedAt });
        b.HasIndex(l => new { l.TenantId, l.ShipmentId, l.CapturedAt });
        b.HasIndex(l => new { l.TenantId, l.ReceivedAt });
    }
}

internal sealed class CurrentVehiclePositionConfiguration : IEntityTypeConfiguration<CurrentVehiclePosition>
{
    public void Configure(EntityTypeBuilder<CurrentVehiclePosition> b)
    {
        b.ToTable("current_vehicle_positions");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.VehicleReference).HasMaxLength(20).IsRequired();
        b.Property(p => p.TripReference).HasMaxLength(40);
        b.Property(p => p.ShipmentReference).HasMaxLength(40);
        b.Property(p => p.DriverName).HasMaxLength(150);
        b.Property(p => p.TransporterReference).HasMaxLength(200);
        b.Property(p => p.Health).HasConversion<string>().HasMaxLength(12);
        b.Property(p => p.NetworkType).HasMaxLength(20);
        b.HasIndex(p => new { p.TenantId, p.VehicleReference }).IsUnique();
        b.HasIndex(p => new { p.TenantId, p.TripReference });
        b.HasIndex(p => new { p.TenantId, p.TransporterId });
    }
}

internal sealed class TrackingDeviceConfiguration : IEntityTypeConfiguration<TrackingDevice>
{
    public void Configure(EntityTypeBuilder<TrackingDevice> b)
    {
        b.ToTable("tracking_devices");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.DeviceId).HasMaxLength(100).IsRequired();
        b.Property(d => d.DriverReference).HasMaxLength(150);
        b.Property(d => d.AppVersion).HasMaxLength(30);
        b.Property(d => d.LastNetworkType).HasMaxLength(20);
        b.Property(d => d.LocationPermission).HasMaxLength(12);
        b.HasIndex(d => new { d.TenantId, d.DeviceId }).IsUnique();
    }
}

internal sealed class ShipmentEventConfiguration : IEntityTypeConfiguration<ShipmentEvent>
{
    public void Configure(EntityTypeBuilder<ShipmentEvent> b)
    {
        b.ToTable("shipment_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(e => e.TripReference).HasMaxLength(40).IsRequired();
        b.Property(e => e.EventType).HasMaxLength(30).IsRequired();
        b.Property(e => e.Description).HasMaxLength(500).IsRequired();
        b.Property(e => e.Source).HasConversion<string>().HasMaxLength(10);
        b.Property(e => e.GeofenceReference).HasMaxLength(60);
        b.Property(e => e.ReasonCode).HasMaxLength(500);
        b.HasIndex(e => new { e.TenantId, e.TrackedShipmentId, e.EventTime });
        b.HasIndex(e => new { e.TenantId, e.TripReference, e.EventType, e.EventTime });
    }
}

internal sealed class MilestoneConfiguration : IEntityTypeConfiguration<Milestone>
{
    public void Configure(EntityTypeBuilder<Milestone> b)
    {
        b.ToTable("milestones");
        b.HasKey(m => m.Id);
        b.Property(m => m.Id).ValueGeneratedNever();
        b.Property(m => m.Type).HasConversion<string>().HasMaxLength(28);
        b.Property(m => m.Label).HasMaxLength(100).IsRequired();
        b.Property(m => m.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(m => m.Source).HasConversion<string>().HasMaxLength(10);
        b.Property(m => m.ReasonCode).HasMaxLength(500);
        b.HasIndex(m => new { m.TenantId, m.TrackedShipmentId, m.Order });
    }
}

internal sealed class GeofenceConfiguration : IEntityTypeConfiguration<Geofence>
{
    public void Configure(EntityTypeBuilder<Geofence> b)
    {
        b.ToTable("geofences");
        b.HasKey(g => g.Id);
        b.Property(g => g.Id).ValueGeneratedNever();
        b.Property(g => g.Code).HasMaxLength(30).IsRequired();
        b.Property(g => g.Name).HasMaxLength(150).IsRequired();
        b.Property(g => g.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(g => g.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(g => g.PolygonJson).HasColumnType("text");
        b.Ignore(g => g.Polygon);
        b.Ignore(g => g.Shape);
        b.HasIndex(g => new { g.TenantId, g.Code }).IsUnique();
        b.HasIndex(g => new { g.TenantId, g.Status, g.Type });
    }
}

internal sealed class GeofencePresenceConfiguration : IEntityTypeConfiguration<GeofencePresence>
{
    public void Configure(EntityTypeBuilder<GeofencePresence> b)
    {
        b.ToTable("geofence_presence");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.State).HasConversion<string>().HasMaxLength(16);
        b.HasIndex(p => new { p.TenantId, p.TrackedShipmentId, p.SubjectId }).IsUnique();
    }
}

internal sealed class GeofenceEventConfiguration : IEntityTypeConfiguration<GeofenceEvent>
{
    public void Configure(EntityTypeBuilder<GeofenceEvent> b)
    {
        b.ToTable("geofence_events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.GeofenceCode).HasMaxLength(60).IsRequired();
        b.Property(e => e.GeofenceName).HasMaxLength(200).IsRequired();
        b.Property(e => e.PlaceType).HasConversion<string>().HasMaxLength(16);
        b.Property(e => e.TripReference).HasMaxLength(40).IsRequired();
        b.Property(e => e.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(e => e.VehicleReference).HasMaxLength(20);
        b.Property(e => e.EventType).HasConversion<string>().HasMaxLength(8);
        b.HasIndex(e => new { e.TenantId, e.TrackedShipmentId, e.DetectedAt });
        b.HasIndex(e => new { e.TenantId, e.GeofenceId, e.DetectedAt });
    }
}

internal sealed class RoutePlanConfiguration : IEntityTypeConfiguration<RoutePlan>
{
    public void Configure(EntityTypeBuilder<RoutePlan> b)
    {
        b.ToTable("routes");
        b.HasKey(r => r.Id);
        b.Property(r => r.Id).ValueGeneratedNever();
        b.Property(r => r.PointsJson).HasColumnType("longtext").IsRequired();
        b.Property(r => r.Source).HasMaxLength(12);
        b.Ignore(r => r.Points);
        b.HasIndex(r => new { r.TenantId, r.TrackedShipmentId }).IsUnique();
    }
}

internal sealed class RouteDeviationConfiguration : IEntityTypeConfiguration<RouteDeviation>
{
    public void Configure(EntityTypeBuilder<RouteDeviation> b)
    {
        b.ToTable("route_deviations");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.TripReference).HasMaxLength(40).IsRequired();
        b.Property(d => d.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(d => d.VehicleReference).HasMaxLength(20);
        b.Property(d => d.Severity).HasConversion<string>().HasMaxLength(14);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        b.Property(d => d.Reason).HasConversion<string?>().HasMaxLength(20);
        b.Property(d => d.ReasonNote).HasMaxLength(500);
        b.HasIndex(d => new { d.TenantId, d.TrackedShipmentId, d.DetectedAt });
        b.HasIndex(d => new { d.TenantId, d.Status, d.Severity });
    }
}

internal sealed class DwellEventConfiguration : IEntityTypeConfiguration<DwellEvent>
{
    public void Configure(EntityTypeBuilder<DwellEvent> b)
    {
        b.ToTable("dwell_events");
        b.HasKey(d => d.Id);
        b.Property(d => d.Id).ValueGeneratedNever();
        b.Property(d => d.TripReference).HasMaxLength(40).IsRequired();
        b.Property(d => d.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(d => d.VehicleReference).HasMaxLength(20);
        b.Property(d => d.Place).HasMaxLength(200);
        b.Property(d => d.Kind).HasConversion<string>().HasMaxLength(14);
        b.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        b.HasIndex(d => new { d.TenantId, d.TrackedShipmentId, d.StartAt });
    }
}

internal sealed class TrackingGapConfiguration : IEntityTypeConfiguration<TrackingGap>
{
    public void Configure(EntityTypeBuilder<TrackingGap> b)
    {
        b.ToTable("tracking_gaps");
        b.HasKey(g => g.Id);
        b.Property(g => g.Id).ValueGeneratedNever();
        b.Property(g => g.TripReference).HasMaxLength(40).IsRequired();
        b.Property(g => g.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(g => g.VehicleReference).HasMaxLength(20);
        b.Property(g => g.Severity).HasConversion<string>().HasMaxLength(14);
        b.Ignore(g => g.IsOpen);
        b.HasIndex(g => new { g.TenantId, g.TrackedShipmentId, g.GapStart });
    }
}

internal sealed class EtaPredictionConfiguration : IEntityTypeConfiguration<EtaPrediction>
{
    public void Configure(EntityTypeBuilder<EtaPrediction> b)
    {
        b.ToTable("eta_predictions");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).ValueGeneratedNever();
        b.Property(p => p.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(p => p.TripReference).HasMaxLength(40).IsRequired();
        b.Property(p => p.RiskLevel).HasConversion<string>().HasMaxLength(10);
        b.Property(p => p.Risk).HasConversion<string>().HasMaxLength(16);
        b.Property(p => p.Source).HasMaxLength(20);
        b.Property(p => p.CalculationVersion).HasMaxLength(20);
        b.HasIndex(p => new { p.TenantId, p.TrackedShipmentId, p.StopId, p.PredictedAt });
    }
}

internal sealed class TrackingAlertConfiguration : IEntityTypeConfiguration<TrackingAlert>
{
    public void Configure(EntityTypeBuilder<TrackingAlert> b)
    {
        b.ToTable("tracking_alerts");
        b.HasKey(a => a.Id);
        b.Property(a => a.Id).ValueGeneratedNever();
        b.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(a => a.Severity).HasConversion<string>().HasMaxLength(14);
        b.Property(a => a.Status).HasConversion<string>().HasMaxLength(14);
        b.Property(a => a.TripReference).HasMaxLength(40).IsRequired();
        b.Property(a => a.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(a => a.VehicleReference).HasMaxLength(20);
        b.Property(a => a.Message).HasMaxLength(600).IsRequired();
        b.Property(a => a.DedupeKey).HasMaxLength(120).IsRequired();
        b.Property(a => a.ResolutionNote).HasMaxLength(500);
        b.HasIndex(a => new { a.TenantId, a.DedupeKey }).IsUnique();
        b.HasIndex(a => new { a.TenantId, a.Status, a.Severity, a.RaisedAt });
        b.HasIndex(a => new { a.TenantId, a.TrackedShipmentId });
    }
}

internal sealed class TrackingExceptionConfiguration : IEntityTypeConfiguration<TrackingException>
{
    public void Configure(EntityTypeBuilder<TrackingException> b)
    {
        b.ToTable("tracking_exceptions");
        b.HasKey(e => e.Id);
        b.Property(e => e.Id).ValueGeneratedNever();
        b.Property(e => e.Number).HasMaxLength(20).IsRequired();
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(e => e.Severity).HasConversion<string>().HasMaxLength(14);
        b.Property(e => e.Status).HasConversion<string>().HasMaxLength(14);
        b.Property(e => e.Description).HasMaxLength(600).IsRequired();
        b.Property(e => e.TripReference).HasMaxLength(40).IsRequired();
        b.Property(e => e.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(e => e.VehicleReference).HasMaxLength(20);
        b.Property(e => e.TransporterReference).HasMaxLength(200);
        b.Property(e => e.Department).HasMaxLength(100);
        b.Property(e => e.EscalatedTo).HasMaxLength(100);
        b.Property(e => e.RootCause).HasMaxLength(500);
        b.Property(e => e.DelayReason).HasConversion<string?>().HasMaxLength(20);
        b.Property(e => e.ActionTaken).HasMaxLength(500);
        b.Ignore(e => e.IsOpen);
        b.HasMany(e => e.Notes).WithOne().HasForeignKey(n => n.ExceptionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(e => e.Notes).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(e => new { e.TenantId, e.Number }).IsUnique();
        b.HasIndex(e => new { e.TenantId, e.Status, e.Severity, e.DueAt });
        b.HasIndex(e => new { e.TenantId, e.TransporterId, e.Status });
        b.HasIndex(e => new { e.TenantId, e.TrackedShipmentId });
    }
}

internal sealed class TrackingExceptionNoteConfiguration : IEntityTypeConfiguration<TrackingExceptionNote>
{
    public void Configure(EntityTypeBuilder<TrackingExceptionNote> b)
    {
        b.ToTable("exception_notes");
        b.HasKey(n => n.Id);
        b.Property(n => n.Id).ValueGeneratedNever();
        b.Property(n => n.Text).HasMaxLength(1000).IsRequired();
        b.HasIndex(n => new { n.TenantId, n.ExceptionId, n.At });
    }
}

internal sealed class CustomerTrackingLinkConfiguration : IEntityTypeConfiguration<CustomerTrackingLink>
{
    public void Configure(EntityTypeBuilder<CustomerTrackingLink> b)
    {
        b.ToTable("customer_tracking_links");
        b.HasKey(l => l.Id);
        b.Property(l => l.Id).ValueGeneratedNever();
        b.Property(l => l.ShipmentReference).HasMaxLength(40).IsRequired();
        b.Property(l => l.CustomerReference).HasMaxLength(64);
        b.Property(l => l.CustomerName).HasMaxLength(200);
        b.Property(l => l.TokenHash).HasMaxLength(64).IsRequired();
        // Links are looked up by the hash of the token alone, before anyone is signed in, so the hash must be unique across tenants.
        b.HasIndex(l => l.TokenHash).IsUnique();
        b.HasIndex(l => new { l.TenantId, l.TrackedShipmentId });
    }
}

internal sealed class TrackingSettingConfiguration : IEntityTypeConfiguration<TrackingSetting>
{
    public void Configure(EntityTypeBuilder<TrackingSetting> b)
    {
        b.ToTable("settings");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.Key).HasMaxLength(60).IsRequired();
        b.Property(s => s.ValueJson).HasColumnType("text").IsRequired();
        b.HasIndex(s => new { s.TenantId, s.Key }).IsUnique();
    }
}

internal sealed class TrackingSyncRecordConfiguration : IEntityTypeConfiguration<TrackingSyncRecord>
{
    public void Configure(EntityTypeBuilder<TrackingSyncRecord> b)
    {
        b.ToTable("sync_records");
        b.HasKey(s => s.Id);
        b.Property(s => s.Id).ValueGeneratedNever();
        b.Property(s => s.ClientKey).HasMaxLength(100).IsRequired();
        b.Property(s => s.Operation).HasMaxLength(20).IsRequired();
        b.Property(s => s.DeviceId).HasMaxLength(100);
        b.Property(s => s.TripReference).HasMaxLength(40);
        b.Property(s => s.ResultJson).HasColumnType("text");
        b.HasIndex(s => new { s.TenantId, s.Operation, s.ClientKey }).IsUnique();
    }
}
