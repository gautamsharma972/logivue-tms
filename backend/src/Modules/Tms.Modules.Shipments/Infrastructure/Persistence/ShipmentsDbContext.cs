using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Infrastructure.Persistence;

public sealed class ShipmentsDbContext(DbContextOptions<ShipmentsDbContext> options, ICurrentUser currentUser)
    : TmsDbContext(options, currentUser)
{
    public const string Schema = "shipments";

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Shipment> Shipments => Set<Shipment>();

    public DbSet<PodDocument> PodDocuments => Set<PodDocument>();

    public DbSet<MilkRunTemplate> MilkRunTemplates => Set<MilkRunTemplate>();

    public DbSet<MilkRunStop> MilkRunStops => Set<MilkRunStop>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<PlanningRun> PlanningRuns => Set<PlanningRun>();

    public DbSet<ShipmentOrder> ShipmentOrders => Set<ShipmentOrder>();

    public DbSet<ProductCompatibilityRule> CompatibilityRules => Set<ProductCompatibilityRule>();

    public DbSet<TenderRound> TenderRounds => Set<TenderRound>();

    public DbSet<TenderInvitee> TenderInvitees => Set<TenderInvitee>();

    public DbSet<TenderEvent> TenderEvents => Set<TenderEvent>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ShipmentsDbContext).Assembly);
    }
}
