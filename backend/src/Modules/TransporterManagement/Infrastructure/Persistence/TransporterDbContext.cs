using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Claims;
using LogiVue.Tms.TransporterManagement.Domain.Audit;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Execution;
using LogiVue.Tms.TransporterManagement.Domain.Pod;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using LogiVue.Tms.TransporterManagement.Domain.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;

namespace LogiVue.Tms.TransporterManagement.Infrastructure.Persistence;

/// <summary>
/// The Transporter Management module's own DbContext. It owns only <c>tm_*</c> tables and keeps its
/// migrations history separate from other modules (registered by AddTransporterManagementInfrastructure).
/// </summary>
public class TransporterDbContext(DbContextOptions<TransporterDbContext> options) : DbContext(options)
{
    /// <summary>Migrations history table name. Must differ from other contexts sharing the same database.</summary>
    public const string MigrationsHistoryTable = "__tm_efmigrations_history";

    public DbSet<Transporter> Transporters => Set<Transporter>();
    public DbSet<TransporterContact> TransporterContacts => Set<TransporterContact>();
    public DbSet<TransporterBranch> TransporterBranches => Set<TransporterBranch>();
    public DbSet<TransporterLane> TransporterLanes => Set<TransporterLane>();
    public DbSet<TransporterCapability> TransporterCapabilities => Set<TransporterCapability>();
    public DbSet<TransporterDocument> TransporterDocuments => Set<TransporterDocument>();
    public DbSet<TransporterVehicle> TransporterVehicles => Set<TransporterVehicle>();
    public DbSet<TransporterDriver> TransporterDrivers => Set<TransporterDriver>();
    public DbSet<ServiceTypeDefinition> ServiceTypes => Set<ServiceTypeDefinition>();
    public DbSet<TransporterUser> TransporterUsers => Set<TransporterUser>();
    public DbSet<TransporterRate> TransporterRates => Set<TransporterRate>();

    public DbSet<Tender> Tenders => Set<Tender>();
    public DbSet<TenderResponse> TenderResponses => Set<TenderResponse>();
    public DbSet<TenderEvent> TenderEvents => Set<TenderEvent>();

    public DbSet<VehiclePlacementRequest> VehiclePlacementRequests => Set<VehiclePlacementRequest>();
    public DbSet<VehiclePlacementEvent> VehiclePlacementEvents => Set<VehiclePlacementEvent>();

    public DbSet<LoadExecution> LoadExecutions => Set<LoadExecution>();
    public DbSet<LoadExecutionEvent> LoadExecutionEvents => Set<LoadExecutionEvent>();

    public DbSet<PodRecord> PodRecords => Set<PodRecord>();

    public DbSet<TransporterClaim> Claims => Set<TransporterClaim>();
    public DbSet<LoadCost> LoadCosts => Set<LoadCost>();
    public DbSet<CapacityDay> CapacityDays => Set<CapacityDay>();

    public DbSet<PerformanceKpi> PerformanceKpis => Set<PerformanceKpi>();
    public DbSet<TransporterScorecard> Scorecards => Set<TransporterScorecard>();
    public DbSet<ScorecardDetail> ScorecardDetails => Set<ScorecardDetail>();
    public DbSet<TransporterRanking> Rankings => Set<TransporterRanking>();

    public DbSet<TransporterAlert> Alerts => Set<TransporterAlert>();
    public DbSet<TransporterException> Exceptions => Set<TransporterException>();

    public DbSet<TransporterPlanningFeedback> PlanningFeedback => Set<TransporterPlanningFeedback>();
    public DbSet<TransporterPlanningRule> PlanningRules => Set<TransporterPlanningRule>();

    public DbSet<TransporterAuditLog> AuditLogs => Set<TransporterAuditLog>();
    public DbSet<TransporterApprovalAction> ApprovalActions => Set<TransporterApprovalAction>();

    public DbSet<TransporterType> TransporterTypes => Set<TransporterType>();
    public DbSet<CapabilityType> CapabilityTypes => Set<CapabilityType>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<ConfigurationSetting> ConfigurationSettings => Set<ConfigurationSetting>();
    public DbSet<OnboardingStep> OnboardingSteps => Set<OnboardingStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TransporterDbContext).Assembly);
    }
}
