using Tms.SharedKernel.Domain;

namespace Tms.Modules.Reports.Domain;

public enum ReportStatus { Active = 1, Disabled = 2 }

public enum ReportType { Dashboard = 1, Operational = 2, Analytical = 3, Drilldown = 4 }

public enum RefreshType { RealTime = 1, NearRealTime = 2, Scheduled = 3, OnDemand = 4 }

public enum FieldType { Text = 1, Whole = 2, Number = 3, Percent = 4, Currency = 5, Date = 6, DateTime = 7, Duration = 8, Boolean = 9, Status = 10 }

/// <summary>A report as this organisation sees it. The report's logic is code, identified by <see cref="Code"/>; everything a person may want to change without a release (name, who may see it, columns, filters, status) is data.</summary>
public sealed class ReportDefinition : AggregateRoot, ITenantScoped
{
    private readonly List<ReportColumn> _columns = [];
    private readonly List<ReportFilter> _filters = [];
    private readonly List<ReportGrouping> _groupings = [];
    private readonly List<ReportSort> _sorts = [];

    private ReportDefinition()
    {
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public string Category { get; private set; } = null!;

    public ReportType Type { get; private set; }

    public string DataSource { get; private set; } = null!;

    public RefreshType Refresh { get; private set; }

    public string RequiredPermission { get; private set; } = null!;

    public ReportStatus Status { get; private set; }

    public bool VendorSafe { get; private set; }

    public int SortOrder { get; private set; }

    public string ExportFormats { get; private set; } = "csv,xlsx";

    public IReadOnlyList<ReportColumn> Columns => _columns;

    public IReadOnlyList<ReportFilter> Filters => _filters;

    public IReadOnlyList<ReportGrouping> Groupings => _groupings;

    public IReadOnlyList<ReportSort> Sorts => _sorts;

    public static ReportDefinition Create(Guid tenantId, ReportSpecSummary spec)
    {
        var d = new ReportDefinition
        {
            TenantId = tenantId,
            Code = spec.Code,
            Name = spec.Name,
            Description = spec.Description,
            Category = spec.Category,
            Type = spec.Type,
            DataSource = spec.DataSource,
            Refresh = spec.Refresh,
            RequiredPermission = spec.Permission,
            Status = ReportStatus.Active,
            VendorSafe = spec.VendorSafe,
            SortOrder = spec.SortOrder,
            ExportFormats = spec.ExportFormats,
        };
        foreach (var c in spec.Columns)
        {
            d._columns.Add(new ReportColumn(tenantId, d.Id, c));
        }

        foreach (var f in spec.Filters)
        {
            d._filters.Add(new ReportFilter(tenantId, d.Id, f));
        }

        foreach (var g in spec.Groupings)
        {
            d._groupings.Add(new ReportGrouping(tenantId, d.Id, g.Field, g.Display, g.Sequence));
        }

        foreach (var s in spec.Sorts)
        {
            d._sorts.Add(new ReportSort(tenantId, d.Id, s.Field, s.Descending, s.Sequence));
        }

        return d;
    }

    /// <summary>Adds what a newer release of the report has that this organisation's copy lacks, without touching what they changed.</summary>
    public bool Reconcile(ReportSpecSummary spec)
    {
        var changed = false;
        foreach (var c in spec.Columns.Where(c => _columns.All(x => x.FieldName != c.Field)))
        {
            _columns.Add(new ReportColumn(TenantId, Id, c));
            changed = true;
        }

        foreach (var f in spec.Filters.Where(f => _filters.All(x => x.FilterName != f.Name)))
        {
            _filters.Add(new ReportFilter(TenantId, Id, f));
            changed = true;
        }

        foreach (var g in spec.Groupings.Where(g => _groupings.All(x => x.FieldName != g.Field)))
        {
            _groupings.Add(new ReportGrouping(TenantId, Id, g.Field, g.Display, g.Sequence));
            changed = true;
        }

        return changed;
    }

    public void Update(string? name, string? description, string? requiredPermission, ReportStatus? status)
    {
        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
        Description = description?.Trim() ?? Description;
        RequiredPermission = string.IsNullOrWhiteSpace(requiredPermission) ? RequiredPermission : requiredPermission.Trim();
        Status = status ?? Status;
    }

    public void SetColumn(string field, string? displayName, bool? visible, int? sequence)
    {
        var column = _columns.FirstOrDefault(c => c.FieldName == field);
        column?.Change(displayName, visible, sequence);
    }
}

public sealed class ReportColumn : Entity, ITenantScoped
{
    private ReportColumn()
    {
    }

    internal ReportColumn(Guid tenantId, Guid reportId, ColumnSpec c)
    {
        TenantId = tenantId;
        ReportDefinitionId = reportId;
        FieldName = c.Field;
        DisplayName = c.Display;
        DataType = c.Type;
        Format = c.Format;
        Sequence = c.Sequence;
        Sortable = c.Sortable;
        Filterable = c.Filterable;
        Visible = c.Visible;
    }

    public Guid TenantId { get; private set; }

    public Guid ReportDefinitionId { get; private set; }

    public string FieldName { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public FieldType DataType { get; private set; }

    public string? Format { get; private set; }

    public int Sequence { get; private set; }

    public bool Sortable { get; private set; }

    public bool Filterable { get; private set; }

    public bool Visible { get; private set; }

    internal void Change(string? displayName, bool? visible, int? sequence)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? DisplayName : displayName.Trim();
        Visible = visible ?? Visible;
        Sequence = sequence ?? Sequence;
    }
}

public sealed class ReportFilter : Entity, ITenantScoped
{
    private ReportFilter()
    {
    }

    internal ReportFilter(Guid tenantId, Guid reportId, FilterSpec f)
    {
        TenantId = tenantId;
        ReportDefinitionId = reportId;
        FilterName = f.Name;
        DataType = f.Type;
        Required = f.Required;
        DefaultValue = f.Default;
        LookupSource = f.Lookup;
        Sequence = f.Sequence;
    }

    public Guid TenantId { get; private set; }

    public Guid ReportDefinitionId { get; private set; }

    public string FilterName { get; private set; } = null!;

    public FieldType DataType { get; private set; }

    public bool Required { get; private set; }

    public string? DefaultValue { get; private set; }

    public string? LookupSource { get; private set; }

    public int Sequence { get; private set; }
}

public sealed class ReportGrouping : Entity, ITenantScoped
{
    private ReportGrouping()
    {
    }

    internal ReportGrouping(Guid tenantId, Guid reportId, string field, string display, int sequence)
    {
        TenantId = tenantId;
        ReportDefinitionId = reportId;
        FieldName = field;
        DisplayName = display;
        Sequence = sequence;
    }

    public Guid TenantId { get; private set; }

    public Guid ReportDefinitionId { get; private set; }

    public string FieldName { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public int Sequence { get; private set; }
}

public sealed class ReportSort : Entity, ITenantScoped
{
    private ReportSort()
    {
    }

    internal ReportSort(Guid tenantId, Guid reportId, string field, bool descending, int sequence)
    {
        TenantId = tenantId;
        ReportDefinitionId = reportId;
        FieldName = field;
        Descending = descending;
        Sequence = sequence;
    }

    public Guid TenantId { get; private set; }

    public Guid ReportDefinitionId { get; private set; }

    public string FieldName { get; private set; } = null!;

    public bool Descending { get; private set; }

    public int Sequence { get; private set; }
}

/// <summary>What a KPI means, in words and as a formula. The calculation itself is code in the KPI engine, keyed by <see cref="KpiCode"/> and <see cref="CalculationVersion"/>.</summary>
public sealed class KpiDefinitionRecord : AggregateRoot, ITenantScoped
{
    private KpiDefinitionRecord()
    {
    }

    public Guid TenantId { get; private set; }

    public string KpiCode { get; private set; } = null!;

    public string KpiName { get; private set; } = null!;

    public string Description { get; private set; } = null!;

    public string FormulaDefinition { get; private set; } = null!;

    public string Unit { get; private set; } = null!;

    public string NumeratorDefinition { get; private set; } = null!;

    public string DenominatorDefinition { get; private set; } = null!;

    public string AggregationMethod { get; private set; } = null!;

    public string ApplicableModule { get; private set; } = null!;

    public string ApplicableServiceTypes { get; private set; } = "FTL,PTL,Dedicated";

    public string SourceOfTruth { get; private set; } = null!;

    public string CalculationVersion { get; private set; } = null!;

    public bool HigherIsBetter { get; private set; }

    public ReportStatus Status { get; private set; }

    public static KpiDefinitionRecord Create(Guid tenantId, KpiDefinition d) => new()
    {
        TenantId = tenantId,
        KpiCode = d.Code,
        KpiName = d.Name,
        Description = d.Description,
        FormulaDefinition = d.Formula,
        Unit = d.Unit.ToString(),
        NumeratorDefinition = d.Numerator,
        DenominatorDefinition = d.Denominator,
        AggregationMethod = d.Aggregation.ToString(),
        ApplicableModule = d.Module,
        SourceOfTruth = d.SourceOfTruth,
        CalculationVersion = d.Version,
        HigherIsBetter = d.HigherIsBetter,
        Status = ReportStatus.Active,
    };

    public void Describe(string? name, string? description)
    {
        KpiName = string.IsNullOrWhiteSpace(name) ? KpiName : name.Trim();
        Description = description?.Trim() ?? Description;
    }

    public void SetStatus(ReportStatus status) => Status = status;
}

public enum ReportJobStatus { Queued = 1, Running = 2, Completed = 3, Failed = 4, Expired = 5, Cancelled = 6 }

/// <summary>An export asked for, built in the background and kept for a limited time. The requester's authority is frozen at the time of asking, so the file holds only what they could see then.</summary>
public sealed class ReportJob : AggregateRoot, ITenantScoped
{
    private ReportJob()
    {
    }

    public Guid TenantId { get; private set; }

    public string JobReference { get; private set; } = null!;

    public string ReportCode { get; private set; } = null!;

    public Guid RequestedBy { get; private set; }

    public string ParametersJson { get; private set; } = null!;

    public string Format { get; private set; } = null!;

    public ReportJobStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? OutputFileReference { get; private set; }

    public string? FileName { get; private set; }

    public string? ContentType { get; private set; }

    public long? SizeBytes { get; private set; }

    public int? RowCount { get; private set; }

    public int Progress { get; private set; }

    public string? ErrorMessage { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public int DownloadCount { get; private set; }

    public DateTimeOffset? LastDownloadedAt { get; private set; }

    public Guid? LastDownloadedBy { get; private set; }

    /// <summary>The requester's permissions and company limits as they were when asking (JSON).</summary>
    public string PrincipalJson { get; private set; } = null!;

    public Guid? SubscriptionId { get; private set; }

    public static ReportJob Create(Guid tenantId, string reference, string reportCode, Guid requestedBy, string parametersJson, string format, string principalJson, DateTimeOffset now, Guid? subscriptionId = null) => new()
    {
        TenantId = tenantId,
        JobReference = reference,
        ReportCode = reportCode,
        RequestedBy = requestedBy,
        ParametersJson = parametersJson,
        Format = format,
        PrincipalJson = principalJson,
        Status = ReportJobStatus.Queued,
        RequestedAt = now,
        SubscriptionId = subscriptionId,
    };

    public bool IsFinished => Status is ReportJobStatus.Completed or ReportJobStatus.Failed or ReportJobStatus.Expired or ReportJobStatus.Cancelled;

    public void Start(DateTimeOffset now)
    {
        Status = ReportJobStatus.Running;
        StartedAt = now;
        Attempts++;
        Progress = 5;
        ErrorMessage = null;
    }

    public void Advance(int progress) => Progress = Math.Clamp(progress, Progress, 99);

    public void Complete(string fileKey, string fileName, string contentType, long size, int rows, DateTimeOffset now, TimeSpan keepFor)
    {
        Status = ReportJobStatus.Completed;
        CompletedAt = now;
        OutputFileReference = fileKey;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = size;
        RowCount = rows;
        Progress = 100;
        ExpiresAt = now + keepFor;
    }

    public void Fail(string message, DateTimeOffset now)
    {
        Status = ReportJobStatus.Failed;
        CompletedAt = now;
        ErrorMessage = message.Length > 480 ? message[..480] : message;
    }

    public bool Cancel(DateTimeOffset now)
    {
        if (IsFinished)
        {
            return false;
        }

        Status = ReportJobStatus.Cancelled;
        CompletedAt = now;
        return true;
    }

    /// <summary>Back to the queue after a failure, so a person (or the scheduler) can retry without asking again.</summary>
    public bool Retry()
    {
        if (Status is not (ReportJobStatus.Failed or ReportJobStatus.Cancelled))
        {
            return false;
        }

        Status = ReportJobStatus.Queued;
        CompletedAt = null;
        ErrorMessage = null;
        Progress = 0;
        return true;
    }

    public void Expire()
    {
        Status = ReportJobStatus.Expired;
        OutputFileReference = null;
    }

    public void RecordDownload(Guid by, DateTimeOffset now)
    {
        DownloadCount++;
        LastDownloadedAt = now;
        LastDownloadedBy = by;
    }
}

public enum ScheduleType { Daily = 1, Weekly = 2, Monthly = 3, Custom = 4 }

/// <summary>A report that is produced and sent on a schedule. <see cref="ScheduleDefinitionJson"/> holds the time of day and, by type, the weekday, day of month or interval.</summary>
public sealed class ReportSubscription : AggregateRoot, ITenantScoped
{
    private ReportSubscription()
    {
    }

    public Guid TenantId { get; private set; }

    public string ReportCode { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public Guid UserId { get; private set; }

    public string ParametersJson { get; private set; } = "{}";

    public ScheduleType ScheduleType { get; private set; }

    public string ScheduleDefinitionJson { get; private set; } = null!;

    public string Format { get; private set; } = "xlsx";

    public string TimeZone { get; private set; } = "Asia/Kolkata";

    public string RecipientsJson { get; private set; } = "[]";

    public bool Active { get; private set; }

    public DateTimeOffset? NextRunAt { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }

    public Guid? LastJobId { get; private set; }

    public int ConsecutiveFailures { get; private set; }

    public string PrincipalJson { get; private set; } = null!;

    public static ReportSubscription Create(
        Guid tenantId, string reportCode, string name, Guid userId, string parametersJson, ScheduleType type, string scheduleJson, string format, string timeZone, string recipientsJson,
        string principalJson, DateTimeOffset? nextRun) => new()
    {
        TenantId = tenantId,
        ReportCode = reportCode,
        Name = name,
        UserId = userId,
        ParametersJson = parametersJson,
        ScheduleType = type,
        ScheduleDefinitionJson = scheduleJson,
        Format = format,
        TimeZone = timeZone,
        RecipientsJson = recipientsJson,
        PrincipalJson = principalJson,
        Active = true,
        NextRunAt = nextRun,
    };

    public void Change(string name, string parametersJson, ScheduleType type, string scheduleJson, string format, string timeZone, string recipientsJson, bool active, string principalJson, DateTimeOffset? nextRun)
    {
        Name = name;
        ParametersJson = parametersJson;
        ScheduleType = type;
        ScheduleDefinitionJson = scheduleJson;
        Format = format;
        TimeZone = timeZone;
        RecipientsJson = recipientsJson;
        Active = active;
        PrincipalJson = principalJson;
        NextRunAt = active ? nextRun : null;
        ConsecutiveFailures = 0;
    }

    public void Ran(Guid jobId, DateTimeOffset now, DateTimeOffset? next)
    {
        LastRunAt = now;
        LastJobId = jobId;
        NextRunAt = next;
        ConsecutiveFailures = 0;
    }

    public void RunFailed(DateTimeOffset? next, int stopAfter)
    {
        ConsecutiveFailures++;
        NextRunAt = next;
        if (ConsecutiveFailures >= stopAfter)
        {
            Active = false;
            NextRunAt = null;
        }
    }
}

/// <summary>A line of the report audit trail: who looked at, ran, exported or scheduled what, and with which filters. Never edited.</summary>
public sealed class ReportAuditEntry : Entity, ITenantScoped
{
    private ReportAuditEntry()
    {
    }

    public Guid TenantId { get; private set; }

    public string ReportCode { get; private set; } = null!;

    public string Action { get; private set; } = null!;

    public Guid? RequestedBy { get; private set; }

    public string? ParametersJson { get; private set; }

    public DateTimeOffset PerformedAtUtc { get; private set; }

    public string? ExportFormat { get; private set; }

    public int? DurationMs { get; private set; }

    public int? RowCount { get; private set; }

    public string? Outcome { get; private set; }

    public string? TraceId { get; private set; }

    public static ReportAuditEntry Create(Guid tenantId, string reportCode, string action, Guid? by, string? parametersJson, DateTimeOffset at, string? exportFormat = null, int? durationMs = null, int? rows = null, string? outcome = null, string? traceId = null) => new()
    {
        TenantId = tenantId,
        ReportCode = reportCode,
        Action = action,
        RequestedBy = by,
        ParametersJson = parametersJson,
        PerformedAtUtc = at,
        ExportFormat = exportFormat,
        DurationMs = durationMs,
        RowCount = rows,
        Outcome = outcome,
        TraceId = traceId,
    };
}

/// <summary>One row per organisation: every threshold and choice the reports use. Values are JSON so a new setting never needs a migration.</summary>
public sealed class ReportSetting : AggregateRoot, ITenantScoped
{
    private ReportSetting()
    {
    }

    public Guid TenantId { get; private set; }

    public string ValueJson { get; private set; } = null!;

    public static ReportSetting Create(Guid tenantId, string json) => new() { TenantId = tenantId, ValueJson = json };

    public void Set(string json) => ValueJson = json;
}

/// <summary>A person's own report choices: favourites, dashboard widgets, default filters and refresh interval.</summary>
public sealed class UserReportPreference : AggregateRoot, ITenantScoped
{
    private UserReportPreference()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    public string FavouritesJson { get; private set; } = "[]";

    public string WidgetsJson { get; private set; } = "[]";

    public string DefaultFiltersJson { get; private set; } = "{}";

    public int RefreshSeconds { get; private set; }

    public static UserReportPreference Create(Guid tenantId, Guid userId) => new() { TenantId = tenantId, UserId = userId };

    public void Set(string? favouritesJson, string? widgetsJson, string? defaultFiltersJson, int? refreshSeconds)
    {
        FavouritesJson = favouritesJson ?? FavouritesJson;
        WidgetsJson = widgetsJson ?? WidgetsJson;
        DefaultFiltersJson = defaultFiltersJson ?? DefaultFiltersJson;
        RefreshSeconds = refreshSeconds ?? RefreshSeconds;
    }
}

/// <summary>The customers, regions or business units a (staff) user is limited to. A user with no rows is not limited on that dimension.</summary>
public sealed class DataScope : Entity, ITenantScoped
{
    private DataScope()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Customer, Region or BusinessUnit.</summary>
    public string Dimension { get; private set; } = null!;

    public string Value { get; private set; } = null!;

    public static DataScope Create(Guid tenantId, Guid userId, string dimension, string value) => new() { TenantId = tenantId, UserId = userId, Dimension = dimension, Value = value.Trim() };
}

/// <summary>A KPI's numerator and denominator for one day (and optionally one transporter), kept so a trend does not need the transactions each time it is drawn.</summary>
public sealed class DailyTransportKpi : Entity, ITenantScoped
{
    private DailyTransportKpi()
    {
    }

    public Guid TenantId { get; private set; }

    public DateOnly Date { get; private set; }

    /// <summary>Guid.Empty for all transporters together.</summary>
    public Guid TransporterId { get; private set; }

    public string KpiCode { get; private set; } = null!;

    public decimal Numerator { get; private set; }

    public decimal Denominator { get; private set; }

    public string CalculationVersion { get; private set; } = null!;

    public DateTimeOffset ComputedAt { get; private set; }

    public static DailyTransportKpi Create(Guid tenantId, DateOnly date, Guid transporterId, string kpiCode, decimal numerator, decimal denominator, string version, DateTimeOffset at) =>
        new() { TenantId = tenantId, Date = date, TransporterId = transporterId, KpiCode = kpiCode, Numerator = numerator, Denominator = denominator, CalculationVersion = version, ComputedAt = at };

    public void Recompute(decimal numerator, decimal denominator, string version, DateTimeOffset at)
    {
        Numerator = numerator;
        Denominator = denominator;
        CalculationVersion = version;
        ComputedAt = at;
    }
}
