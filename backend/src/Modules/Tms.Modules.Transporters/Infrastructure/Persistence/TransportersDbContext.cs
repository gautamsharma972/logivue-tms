using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Infrastructure.Persistence;

public sealed class TransportersDbContext(DbContextOptions<TransportersDbContext> options, ICurrentUser currentUser, IFieldEncryptor encryptor)
    : TmsDbContext(options, currentUser)
{
    public const string Schema = "transporters";

    public DbSet<Transporter> Transporters => Set<Transporter>();

    public DbSet<VehicleType> VehicleTypes => Set<VehicleType>();

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();

    public DbSet<Driver> Drivers => Set<Driver>();

    public DbSet<ComplianceDocument> Documents => Set<ComplianceDocument>();

    public DbSet<SequenceCounter> Sequences => Set<SequenceCounter>();

    public DbSet<TenderInvitation> Invitations => Set<TenderInvitation>();

    public DbSet<LoadExecution> Executions => Set<LoadExecution>();

    public DbSet<ExecutionEvent> ExecutionEvents => Set<ExecutionEvent>();

    public DbSet<PerformanceKpi> Kpis => Set<PerformanceKpi>();

    public DbSet<Scorecard> Scorecards => Set<Scorecard>();

    public DbSet<TransporterLane> Lanes => Set<TransporterLane>();

    public DbSet<TransporterSetting> Settings => Set<TransporterSetting>();

    public DbSet<TransporterCapability> Capabilities => Set<TransporterCapability>();

    public DbSet<PlanningRule> PlanningRules => Set<PlanningRule>();

    public DbSet<PlanningFeedback> Feedback => Set<PlanningFeedback>();

    public DbSet<VehiclePlacement> Placements => Set<VehiclePlacement>();

    public DbSet<PlacementEvent> PlacementEvents => Set<PlacementEvent>();

    public DbSet<ClaimRecord> Claims => Set<ClaimRecord>();

    public DbSet<LoadCost> Costs => Set<LoadCost>();

    public DbSet<CapacityDay> Capacity => Set<CapacityDay>();

    public DbSet<TransporterAlert> Alerts => Set<TransporterAlert>();

    public DbSet<TransporterContact> Contacts => Set<TransporterContact>();

    public DbSet<TransporterBranch> Branches => Set<TransporterBranch>();

    public DbSet<ProofPerformance> ProofPerformances => Set<ProofPerformance>();

    public DbSet<MasterItem> MasterItems => Set<MasterItem>();

    public DbSet<DocumentRule> DocumentRules => Set<DocumentRule>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransportersDbContext).Assembly);

        // Bank account numbers are encrypted at rest (random nonce, so the column is deliberately not indexable).
        modelBuilder.Entity<Transporter>().Property(t => t.BankAccountNumber)
            .HasMaxLength(160)
            .HasConversion(
                v => v == null ? null : encryptor.Encrypt(v),
                v => v == null ? null : encryptor.Decrypt(v));
    }
}
