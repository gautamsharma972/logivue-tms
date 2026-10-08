using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tms.Modules.Reports.Domain;

namespace Tms.Modules.Reports.Infrastructure.Persistence.Configurations;

internal sealed class ReportDefinitionConfiguration : IEntityTypeConfiguration<ReportDefinition>
{
    public void Configure(EntityTypeBuilder<ReportDefinition> b)
    {
        b.ToTable("report_definitions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Code).HasMaxLength(60).IsRequired();
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        b.Property(x => x.Category).HasMaxLength(80).IsRequired();
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.DataSource).HasMaxLength(120).IsRequired();
        b.Property(x => x.Refresh).HasConversion<string>().HasMaxLength(16);
        b.Property(x => x.RequiredPermission).HasMaxLength(64).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.ExportFormats).HasMaxLength(40).IsRequired();
        b.HasMany(x => x.Columns).WithOne().HasForeignKey(c => c.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Columns).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Filters).WithOne().HasForeignKey(c => c.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Filters).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Groupings).WithOne().HasForeignKey(c => c.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Groupings).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasMany(x => x.Sorts).WithOne().HasForeignKey(c => c.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Sorts).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

internal sealed class ReportColumnConfiguration : IEntityTypeConfiguration<ReportColumn>
{
    public void Configure(EntityTypeBuilder<ReportColumn> b)
    {
        b.ToTable("report_columns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FieldName).HasMaxLength(60).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        b.Property(x => x.DataType).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.Format).HasMaxLength(40);
        b.HasIndex(x => new { x.ReportDefinitionId, x.FieldName }).IsUnique();
    }
}

internal sealed class ReportFilterConfiguration : IEntityTypeConfiguration<ReportFilter>
{
    public void Configure(EntityTypeBuilder<ReportFilter> b)
    {
        b.ToTable("report_filters");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FilterName).HasMaxLength(60).IsRequired();
        b.Property(x => x.DataType).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.DefaultValue).HasMaxLength(200);
        b.Property(x => x.LookupSource).HasMaxLength(60);
        b.HasIndex(x => new { x.ReportDefinitionId, x.FilterName }).IsUnique();
    }
}

internal sealed class ReportGroupingConfiguration : IEntityTypeConfiguration<ReportGrouping>
{
    public void Configure(EntityTypeBuilder<ReportGrouping> b)
    {
        b.ToTable("report_groupings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FieldName).HasMaxLength(60).IsRequired();
        b.Property(x => x.DisplayName).HasMaxLength(120).IsRequired();
        b.HasIndex(x => new { x.ReportDefinitionId, x.FieldName }).IsUnique();
    }
}

internal sealed class ReportSortConfiguration : IEntityTypeConfiguration<ReportSort>
{
    public void Configure(EntityTypeBuilder<ReportSort> b)
    {
        b.ToTable("report_sorts");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FieldName).HasMaxLength(60).IsRequired();
        b.HasIndex(x => new { x.ReportDefinitionId, x.Sequence });
    }
}

internal sealed class KpiDefinitionConfiguration : IEntityTypeConfiguration<KpiDefinitionRecord>
{
    public void Configure(EntityTypeBuilder<KpiDefinitionRecord> b)
    {
        b.ToTable("kpi_definitions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.KpiCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.KpiName).HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasMaxLength(600).IsRequired();
        b.Property(x => x.FormulaDefinition).HasMaxLength(400).IsRequired();
        b.Property(x => x.Unit).HasMaxLength(12).IsRequired();
        b.Property(x => x.NumeratorDefinition).HasMaxLength(500).IsRequired();
        b.Property(x => x.DenominatorDefinition).HasMaxLength(500).IsRequired();
        b.Property(x => x.AggregationMethod).HasMaxLength(16).IsRequired();
        b.Property(x => x.ApplicableModule).HasMaxLength(40).IsRequired();
        b.Property(x => x.ApplicableServiceTypes).HasMaxLength(60).IsRequired();
        b.Property(x => x.SourceOfTruth).HasMaxLength(200).IsRequired();
        b.Property(x => x.CalculationVersion).HasMaxLength(10).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        b.HasIndex(x => new { x.TenantId, x.KpiCode }).IsUnique();
    }
}

internal sealed class ReportJobConfiguration : IEntityTypeConfiguration<ReportJob>
{
    public void Configure(EntityTypeBuilder<ReportJob> b)
    {
        b.ToTable("report_jobs");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.JobReference).HasMaxLength(24).IsRequired();
        b.Property(x => x.ReportCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.ParametersJson).HasColumnType("json").IsRequired();
        b.Property(x => x.PrincipalJson).HasColumnType("json").IsRequired();
        b.Property(x => x.Format).HasMaxLength(8).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
        b.Property(x => x.OutputFileReference).HasMaxLength(200);
        b.Property(x => x.FileName).HasMaxLength(200);
        b.Property(x => x.ContentType).HasMaxLength(100);
        b.Property(x => x.ErrorMessage).HasMaxLength(500);
        b.HasIndex(x => new { x.TenantId, x.JobReference }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.RequestedBy, x.RequestedAt });
        b.HasIndex(x => new { x.Status, x.RequestedAt });
    }
}

internal sealed class ReportSubscriptionConfiguration : IEntityTypeConfiguration<ReportSubscription>
{
    public void Configure(EntityTypeBuilder<ReportSubscription> b)
    {
        b.ToTable("report_subscriptions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ReportCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.Name).HasMaxLength(160).IsRequired();
        b.Property(x => x.ParametersJson).HasColumnType("json").IsRequired();
        b.Property(x => x.ScheduleType).HasConversion<string>().HasMaxLength(10);
        b.Property(x => x.ScheduleDefinitionJson).HasColumnType("json").IsRequired();
        b.Property(x => x.Format).HasMaxLength(8).IsRequired();
        b.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        b.Property(x => x.RecipientsJson).HasColumnType("json").IsRequired();
        b.Property(x => x.PrincipalJson).HasColumnType("json").IsRequired();
        b.HasIndex(x => new { x.Active, x.NextRunAt });
        b.HasIndex(x => new { x.TenantId, x.UserId });
    }
}

internal sealed class ReportAuditConfiguration : IEntityTypeConfiguration<ReportAuditEntry>
{
    public void Configure(EntityTypeBuilder<ReportAuditEntry> b)
    {
        b.ToTable("report_audit");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ReportCode).HasMaxLength(60).IsRequired();
        b.Property(x => x.Action).HasMaxLength(40).IsRequired();
        b.Property(x => x.ParametersJson).HasColumnType("json");
        b.Property(x => x.ExportFormat).HasMaxLength(8);
        b.Property(x => x.Outcome).HasMaxLength(200);
        b.Property(x => x.TraceId).HasMaxLength(64);
        b.HasIndex(x => new { x.TenantId, x.PerformedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ReportCode, x.PerformedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.RequestedBy, x.PerformedAtUtc });
    }
}

internal sealed class ReportSettingConfiguration : IEntityTypeConfiguration<ReportSetting>
{
    public void Configure(EntityTypeBuilder<ReportSetting> b)
    {
        b.ToTable("report_settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ValueJson).HasColumnType("json").IsRequired();
        b.HasIndex(x => x.TenantId).IsUnique();
    }
}

internal sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserReportPreference>
{
    public void Configure(EntityTypeBuilder<UserReportPreference> b)
    {
        b.ToTable("user_preferences");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.FavouritesJson).HasColumnType("json").IsRequired();
        b.Property(x => x.WidgetsJson).HasColumnType("json").IsRequired();
        b.Property(x => x.DefaultFiltersJson).HasColumnType("json").IsRequired();
        b.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
    }
}

internal sealed class DataScopeConfiguration : IEntityTypeConfiguration<DataScope>
{
    public void Configure(EntityTypeBuilder<DataScope> b)
    {
        b.ToTable("data_scopes");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Dimension).HasMaxLength(20).IsRequired();
        b.Property(x => x.Value).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.UserId, x.Dimension, x.Value }).IsUnique();
    }
}

internal sealed class DailyTransportKpiConfiguration : IEntityTypeConfiguration<DailyTransportKpi>
{
    public void Configure(EntityTypeBuilder<DailyTransportKpi> b)
    {
        b.ToTable("daily_transport_kpi");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.KpiCode).HasMaxLength(40).IsRequired();
        b.Property(x => x.Numerator).HasPrecision(20, 4);
        b.Property(x => x.Denominator).HasPrecision(20, 4);
        b.Property(x => x.CalculationVersion).HasMaxLength(10).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.KpiCode, x.TransporterId, x.Date }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Date });
    }
}
