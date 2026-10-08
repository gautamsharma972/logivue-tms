using Microsoft.EntityFrameworkCore;
using Tms.Modules.Reports.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Reports.Infrastructure.Persistence;

public sealed class ReportsDbContext(DbContextOptions<ReportsDbContext> options, ICurrentUser currentUser) : TmsDbContext(options, currentUser)
{
    /// <summary>Tables are named <c>rpt_*</c>.</summary>
    public const string Schema = "rpt";

    public DbSet<ReportDefinition> Definitions => Set<ReportDefinition>();

    public DbSet<ReportColumn> Columns => Set<ReportColumn>();

    public DbSet<ReportFilter> Filters => Set<ReportFilter>();

    public DbSet<ReportGrouping> Groupings => Set<ReportGrouping>();

    public DbSet<ReportSort> Sorts => Set<ReportSort>();

    public DbSet<KpiDefinitionRecord> Kpis => Set<KpiDefinitionRecord>();

    public DbSet<ReportJob> Jobs => Set<ReportJob>();

    public DbSet<ReportSubscription> Subscriptions => Set<ReportSubscription>();

    public DbSet<ReportAuditEntry> AuditEntries => Set<ReportAuditEntry>();

    public DbSet<ReportSetting> Settings => Set<ReportSetting>();

    public DbSet<UserReportPreference> Preferences => Set<UserReportPreference>();

    public DbSet<DataScope> DataScopes => Set<DataScope>();

    public DbSet<DailyTransportKpi> DailyKpis => Set<DailyTransportKpi>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportsDbContext).Assembly);
    }
}
