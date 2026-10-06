using Microsoft.EntityFrameworkCore;
using Tms.Modules.Tracking.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Tracking.Infrastructure.Persistence;

public sealed class TrackingDbContext(DbContextOptions<TrackingDbContext> options, ICurrentUser currentUser) : TmsDbContext(options, currentUser)
{
    /// <summary>Tables are prefixed <c>st_</c> (shipment tracking): <c>st_tracking_sessions</c>, <c>st_tracking_locations</c>…</summary>
    public const string Schema = "st";

    public DbSet<TrackedShipment> Shipments => Set<TrackedShipment>();

    public DbSet<ShipmentStop> Stops => Set<ShipmentStop>();

    public DbSet<TrackingSession> Sessions => Set<TrackingSession>();

    public DbSet<TrackingLocation> Locations => Set<TrackingLocation>();

    public DbSet<CurrentVehiclePosition> Positions => Set<CurrentVehiclePosition>();

    public DbSet<TrackingDevice> Devices => Set<TrackingDevice>();

    public DbSet<ShipmentEvent> Events => Set<ShipmentEvent>();

    public DbSet<Milestone> Milestones => Set<Milestone>();

    public DbSet<Geofence> Geofences => Set<Geofence>();

    public DbSet<GeofencePresence> Presences => Set<GeofencePresence>();

    public DbSet<GeofenceEvent> GeofenceEvents => Set<GeofenceEvent>();

    public DbSet<RoutePlan> Routes => Set<RoutePlan>();

    public DbSet<RouteDeviation> Deviations => Set<RouteDeviation>();

    public DbSet<DwellEvent> Dwells => Set<DwellEvent>();

    public DbSet<TrackingGap> Gaps => Set<TrackingGap>();

    public DbSet<EtaPrediction> EtaPredictions => Set<EtaPrediction>();

    public DbSet<TrackingAlert> Alerts => Set<TrackingAlert>();

    public DbSet<TrackingException> Exceptions => Set<TrackingException>();

    public DbSet<TrackingExceptionNote> ExceptionNotes => Set<TrackingExceptionNote>();

    public DbSet<CustomerTrackingLink> Links => Set<CustomerTrackingLink>();

    public DbSet<TrackingSetting> Settings => Set<TrackingSetting>();

    public DbSet<TrackingSyncRecord> SyncRecords => Set<TrackingSyncRecord>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TrackingDbContext).Assembly);
    }
}
