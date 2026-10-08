using System.Text.Json;
using Tms.Modules.Reports.Domain;

namespace Tms.Modules.Reports.Application;

public sealed record SortRequest(string Field, string? Direction = "ASC");

/// <summary>Run a report. Filters are the common filter names (fromDate, transporter, lane …) plus the report's own; "f_{column}" narrows a column's text.</summary>
public sealed record ReportRequest(
    string? ReportCode = null,
    Dictionary<string, JsonElement>? Filters = null,
    IReadOnlyList<string>? GroupBy = null,
    IReadOnlyList<SortRequest>? Sort = null,
    int Page = 1,
    int PageSize = 50,
    bool Refresh = false);

public sealed record ColumnDto(string Field, string DisplayName, string DataType, string? Format, int Sequence, bool Sortable, bool Filterable, bool Visible);

public sealed record FilterDto(string Name, string DataType, bool Required, string? DefaultValue, string? LookupSource, int Sequence);

public sealed record GroupingDto(string Field, string DisplayName);

public sealed record SortDto(string Field, bool Descending);

public sealed record DrillDto(string Field, string TargetReport, string Label, IReadOnlyDictionary<string, string> Map);

public sealed record ReportSummaryDto(
    string ReportCode, string Name, string Description, string Category, string ReportType, string DataSource, string Refresh, string RequiredPermission, string Status,
    bool CanOpen, bool VendorSafe, IReadOnlyList<string> ExportFormats, bool IsFavourite);

public sealed record ReportMetadataDto(
    string ReportCode,
    string Name,
    string Description,
    string Category,
    string ReportType,
    string DataSource,
    string Refresh,
    string RequiredPermission,
    string Status,
    IReadOnlyList<ColumnDto> Columns,
    IReadOnlyList<FilterDto> Filters,
    IReadOnlyList<GroupingDto> Grouping,
    IReadOnlyList<GroupingDto> AvailableGrouping,
    IReadOnlyList<string> DefaultGroupBy,
    IReadOnlyList<SortDto> Sorting,
    IReadOnlyList<DrillDto> Drills,
    IReadOnlyList<string> ExportFormats,
    bool CanExport,
    bool CanSchedule,
    bool IsFavourite,
    bool SupportsComparison,
    string? Note,
    long Version = 0);

public sealed record PeriodDto(DateOnly From, DateOnly To, DateOnly? PreviousFrom, DateOnly? PreviousTo, DateOnly? LastYearFrom, DateOnly? LastYearTo, string Kind);

public sealed record TotalDto(string Key, string Label, object? Value, string? Unit, string? DrillReport, IReadOnlyDictionary<string, string>? DrillFilters, string? Tone);

public sealed record ReportResult(
    string ReportCode,
    string ReportName,
    string ReportType,
    DateTimeOffset GeneratedAtUtc,
    int DataFreshnessSeconds,
    int DurationMs,
    int TotalRows,
    int Page,
    int PageSize,
    IReadOnlyList<ColumnDto> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    IReadOnlyDictionary<string, object?> Summary,
    IReadOnlyList<TotalDto> Totals,
    IReadOnlyList<KpiCard> Cards,
    IReadOnlyList<ChartData> Charts,
    IReadOnlyList<ReportSection> Sections,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, string> FiltersApplied,
    IReadOnlyList<string> GroupedBy,
    IReadOnlyList<DrillDto> Drills,
    PeriodDto Period,
    string CalculationVersion,
    string DataSourceMode,
    string Refresh,
    bool FromCache);

public sealed record KpiDefinitionDto(
    string KpiCode, string KpiName, string Description, string Formula, string Unit, string Numerator, string Denominator, string Aggregation, string ApplicableModule, string ApplicableServiceTypes,
    string SourceOfTruth, string CalculationVersion, bool HigherIsBetter, string Status);

public sealed record KpiRequest(string Code, Dictionary<string, JsonElement>? Filters = null, string? Period = null, string? Compare = null);

public sealed record KpiValueDto(string Code, string Name, string Unit, decimal? Value, decimal Numerator, decimal Denominator, bool Measurable, int Excluded, string? Note, DateOnly From, DateOnly To, string CalculationVersion);

public sealed record KpiOutcome(KpiValueDto Current, KpiValueDto? Previous, KpiValueDto? SamePeriodLastYear, decimal? Change, decimal? ChangePct, string Trend, IReadOnlyDictionary<string, string> Dimensions);

public sealed record ExportRequest(
    Dictionary<string, JsonElement>? Filters = null,
    IReadOnlyList<string>? GroupBy = null,
    IReadOnlyList<SortRequest>? Sort = null,
    string Format = "xlsx",
    bool Background = false);

public sealed record ReportJobDto(
    Guid Id, string JobReference, string ReportCode, string ReportName, string Format, string Status, DateTimeOffset RequestedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    int Progress, int? RowCount, long? SizeBytes, string? FileName, string? ErrorMessage, DateTimeOffset? ExpiresAt, int DownloadCount, bool CanDownload, bool FromSchedule);

/// <summary>An export asked for. Small ones come back at once (the file's job is already Completed); large ones are queued.</summary>
public sealed record ExportOutcome(ReportJobDto Job, bool Immediate);

public sealed record SaveSubscriptionRequest(
    string ReportCode,
    string? Name,
    Dictionary<string, JsonElement>? Filters,
    string ScheduleType,
    ScheduleDefinition Schedule,
    string? Format,
    string? TimeZone,
    IReadOnlyList<string>? Recipients,
    bool Active = true,
    long? Version = null);

public sealed record SubscriptionDto(
    Guid Id, string ReportCode, string ReportName, string Name, Dictionary<string, string> Filters, string ScheduleType, ScheduleDefinition Schedule, string Format, string TimeZone, IReadOnlyList<string> Recipients,
    bool Active, DateTimeOffset? NextRunAt, DateTimeOffset? LastRunAt, int ConsecutiveFailures, Guid UserId, string? UserName, long Version, string? Summary);

public sealed record PreferenceDto(IReadOnlyList<string> Favourites, IReadOnlyList<string> Widgets, Dictionary<string, string> DefaultFilters, int RefreshSeconds);

public sealed record SavePreferenceRequest(IReadOnlyList<string>? Favourites, IReadOnlyList<string>? Widgets, Dictionary<string, string>? DefaultFilters, int? RefreshSeconds);

public sealed record DataScopeDto(Guid UserId, string Dimension, IReadOnlyList<string> Values);

public sealed record SaveDataScopeRequest(Guid UserId, string Dimension, IReadOnlyList<string> Values);

public sealed record UpdateReportRequest(string? Name, string? Description, string? RequiredPermission, string? Status, IReadOnlyList<UpdateColumnRequest>? Columns, long Version);

public sealed record UpdateColumnRequest(string Field, string? DisplayName, bool? Visible, int? Sequence);

public sealed record UpdateKpiRequest(string? Name, string? Description, string? Status, long Version);

public sealed record AuditEntryDto(Guid Id, string ReportCode, string Action, Guid? RequestedBy, string? UserName, string? Parameters, DateTimeOffset PerformedAtUtc, string? ExportFormat, int? DurationMs, int? RowCount, string? Outcome);

public sealed record LookupDto(string Name, IReadOnlyList<string> Values);

public sealed record DataSourceStatusDto(string Mode, IReadOnlyDictionary<string, string> Providers, bool DemoAvailable);

public sealed record SettingsDto(ReportSettings Settings, long Version);

public sealed record SaveSettingsRequest(ReportSettings Settings, long Version);
