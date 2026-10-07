using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Infrastructure.Persistence;

public sealed class ContractsDbContext(DbContextOptions<ContractsDbContext> options, ICurrentUser currentUser)
    : TmsDbContext(options, currentUser)
{
    public const string Schema = "contracts";

    public DbSet<Contract> Contracts => Set<Contract>();

    public DbSet<RateCard> RateCards => Set<RateCard>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<DieselPrice> DieselPrices => Set<DieselPrice>();

    public DbSet<ContractDocument> Documents => Set<ContractDocument>();

    public DbSet<ExpiryAlert> ExpiryAlerts => Set<ExpiryAlert>();

    public DbSet<DphRule> DphRules => Set<DphRule>();

    public DbSet<DphPeriodSnapshot> DphSnapshots => Set<DphPeriodSnapshot>();

    public DbSet<AccessorialType> AccessorialTypes => Set<AccessorialType>();

    public DbSet<ContractAccessorial> ContractAccessorials => Set<ContractAccessorial>();

    public DbSet<ContractCapacity> Capacities => Set<ContractCapacity>();

    public DbSet<ContractSla> Slas => Set<ContractSla>();

    public DbSet<FreightRating> Ratings => Set<FreightRating>();

    public DbSet<RatingComponent> RatingComponents => Set<RatingComponent>();

    public DbSet<RatingExclusion> RatingExclusions => Set<RatingExclusion>();

    public DbSet<RateImportBatch> ImportBatches => Set<RateImportBatch>();

    public DbSet<RateImportRow> ImportRows => Set<RateImportRow>();

    public DbSet<SequenceCounter> Sequences => Set<SequenceCounter>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContractsDbContext).Assembly);
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        // RateCard is tenant-filtered like its contract; loading one through the other is intended and always consistent.
        optionsBuilder.ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
}
