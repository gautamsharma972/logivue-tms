using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Reports.Domain;

public sealed record ColumnSpec(string Field, string Display, FieldType Type, int Sequence, bool Sortable = true, bool Filterable = false, string? Format = null, bool Visible = true);

public sealed record FilterSpec(string Name, FieldType Type, int Sequence, bool Required = false, string? Default = null, string? Lookup = null);

public sealed record GroupingSpec(string Field, string Display, int Sequence);

public sealed record SortSpec(string Field, bool Descending, int Sequence);

/// <summary>What is stored in the report tables. The same record seeds a new organisation and reconciles an existing one after a release.</summary>
public sealed record ReportSpecSummary(
    string Code,
    string Name,
    string Description,
    string Category,
    ReportType Type,
    string DataSource,
    RefreshType Refresh,
    string Permission,
    bool VendorSafe,
    int SortOrder,
    string ExportFormats,
    IReadOnlyList<ColumnSpec> Columns,
    IReadOnlyList<FilterSpec> Filters,
    IReadOnlyList<GroupingSpec> Groupings,
    IReadOnlyList<SortSpec> Sorts);

public enum MeasureKind { Sum, Avg, Min, Max, Count, Ratio }

/// <summary>
/// How a column is combined when rows are grouped. A ratio is always re-derived from summed numerator and denominator columns,
/// never averaged from percentages, so a grouped figure is the figure the whole population would give.
/// </summary>
public sealed record MeasureSpec(string Field, MeasureKind Kind, string? Numerator = null, string? Denominator = null, decimal Scale = 1m);

/// <summary>Clicking <see cref="Field"/> opens <see cref="TargetReport"/> keeping the current filters; <see cref="Map"/> adds filters from the row (target filter → source field), a value starting with "=" is a fixed value.</summary>
public sealed record DrillSpec(string Field, string TargetReport, string Label, IReadOnlyDictionary<string, string> Map);

public sealed record CardSpec(string KpiCode, string? DrillReport = null, IReadOnlyDictionary<string, string>? DrillFilters = null);

public sealed class ReportRow : Dictionary<string, object?>
{
    public ReportRow()
        : base(StringComparer.Ordinal)
    {
    }

    public ReportRow(IDictionary<string, object?> values)
        : base(values, StringComparer.Ordinal)
    {
    }
}

/// <summary>One KPI as a card: current value, the earlier periods it is compared with, and where a click goes.</summary>
public sealed record KpiCard(
    string Code,
    string Name,
    string Unit,
    decimal? Value,
    decimal? Numerator,
    decimal? Denominator,
    bool Measurable,
    string? Note,
    decimal? Previous,
    decimal? SamePeriodLastYear,
    decimal? Change,
    decimal? ChangePct,
    string Trend,
    string Assessment,
    string CalculationVersion,
    bool HigherIsBetter,
    string? DrillReport,
    IReadOnlyDictionary<string, string>? DrillFilters);

public sealed record ChartSeries(string Key, string Name, string? Colour = null);

/// <summary>A reference line on a chart (a threshold, an average) on the x or y axis.</summary>
public sealed record ChartLine(string Axis, decimal Value, string Label);

/// <summary>A chart the page draws from rows. <see cref="Kind"/> is one of line, bar, stackedBar, donut, heatmap, scatter or map.</summary>
public sealed record ChartData(
    string Id,
    string Title,
    string Kind,
    string? XField,
    IReadOnlyList<ChartSeries> Series,
    IReadOnlyList<ReportRow> Data,
    string? Note = null,
    string? YLabel = null,
    string? XLabel = null,
    string? DrillReport = null,
    IReadOnlyDictionary<string, string>? DrillMap = null,
    IReadOnlyList<ChartLine>? Lines = null);

public sealed record SectionItem(string Label, object? Value, string? Format = null, string? DrillReport = null, IReadOnlyDictionary<string, string>? DrillFilters = null);

/// <summary>A titled group of facts, for pages that read across modules (Shipment 360).</summary>
public sealed record ReportSection(string Key, string Title, string? Module, IReadOnlyList<SectionItem> Items, IReadOnlyList<ReportRow>? Table = null, IReadOnlyList<ColumnSpec>? TableColumns = null);

/// <summary>A plain total or count shown beside a report (not a KPI: it has no definition of its own, it is just the sum of what is listed).</summary>
public sealed record SummaryItem(string Key, string Label, object? Value, string? Unit = null, string? DrillReport = null, IReadOnlyDictionary<string, string>? DrillFilters = null, string? Tone = null);

/// <summary>What a report's code produces before the framework applies filters, grouping, sorting and paging.</summary>
public sealed class ReportData
{
    public List<ReportRow> Rows { get; } = [];

    public List<KpiCard> Cards { get; } = [];

    public List<ChartData> Charts { get; } = [];

    public List<ReportSection> Sections { get; } = [];

    public List<string> Notes { get; } = [];

    public List<SummaryItem> Totals { get; } = [];

    /// <summary>True when the rows already are the final grain (no regrouping offered), e.g. a dashboard's small tables.</summary>
    public bool Fixed { get; set; }
}

public delegate Task<ReportData> ReportBuilder(ReportContext context, CancellationToken cancellationToken);

/// <summary>A report as code: identity, shape and the function that produces its data. The tables hold the editable copy of everything except the function.</summary>
public sealed class ReportSpec
{
    public required string Code { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string Category { get; init; }

    public required ReportType Type { get; init; }

    public required string DataSource { get; init; }

    public RefreshType Refresh { get; init; } = RefreshType.OnDemand;

    public required string Permission { get; init; }

    public bool VendorSafe { get; init; }

    public int SortOrder { get; init; }

    public string ExportFormats { get; init; } = "csv,xlsx";

    public IReadOnlyList<ColumnSpec> Columns { get; init; } = [];

    public IReadOnlyList<FilterSpec> Filters { get; init; } = [];

    public IReadOnlyList<GroupingSpec> Groupings { get; init; } = [];

    public IReadOnlyList<SortSpec> Sorts { get; init; } = [];

    public IReadOnlyList<MeasureSpec> Measures { get; init; } = [];

    public IReadOnlyList<DrillSpec> Drills { get; init; } = [];

    /// <summary>Group by these when the request does not say. Empty = show rows as built.</summary>
    public IReadOnlyList<string> DefaultGroupBy { get; init; } = [];

    /// <summary>Dimensions the rows carry, so a user limited to some customers, regions or business units can be held to them. A report without a dimension cannot be shown to someone limited on it.</summary>
    public IReadOnlyList<string> ScopeDimensions { get; init; } = [];

    /// <summary>The report shows KPI cards with comparisons, so the previous period and the same period last year are read too.</summary>
    public bool Comparison { get; init; }

    /// <summary>How many buckets of history a trend chart on this report needs (0 = none).</summary>
    public int TrendBuckets { get; init; }

    /// <summary>When this filter is present the period limit is lifted (a single shipment is read whenever it happened).</summary>
    public string? WideOnFilter { get; init; }

    public required ReportBuilder Build { get; init; }

    public ReportSpecSummary Summary() => new(Code, Name, Description, Category, Type, DataSource, Refresh, Permission, VendorSafe, SortOrder, ExportFormats, Columns, Filters, Groupings, Sorts);
}
