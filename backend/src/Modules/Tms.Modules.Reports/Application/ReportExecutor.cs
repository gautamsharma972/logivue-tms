using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Tms.Modules.Reports.Domain;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Reports.Application;

/// <summary>
/// Runs a report: checks who may, applies the caller's limits and the filters, lets the report's code build its data from the providers' facts,
/// then groups, sorts, filters columns and pages it. Every report goes through here, so access, auditing, timing and the standard error are never repeated.
/// </summary>
internal sealed class ReportExecutor(
    ReportDefinitionService definitions,
    ReportSettingsStore settingsStore,
    ReportingProviderResolver resolver,
    PrincipalFactory principals,
    ReportAuditor auditor,
    DailyKpiTrendSource trends,
    IMemoryCache cache,
    TimeProvider clock,
    ILogger<ReportExecutor> logger)
{
    public const string CalculationVersion = "1.0";

    /// <summary>Everything a caller or a job needs to run a report as a specific principal.</summary>
    public async Task<Result<ReportResult>> ExecuteAsync(string reportCode, ReportRequest request, ReportPrincipal? principal, bool audit, CancellationToken cancellationToken, int? pageLimit = null)
    {
        principal ??= await principals.CurrentAsync(cancellationToken);
        if (principal is null)
        {
            return ReportAccess.Forbidden;
        }

        var definition = await definitions.FindAsync(reportCode, cancellationToken);
        var spec = ReportCatalogue.Find(reportCode);
        if (definition is null || spec is null)
        {
            return ReportAccess.NotFound;
        }

        if (!ReportAccess.CanRun(definition, principal))
        {
            return ReportAccess.Forbidden;
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        var filters = ReportFilters.FromJson(request.Filters);
        foreach (var f in definition.Filters.Where(f => f.DefaultValue is not null && filters.Get(f.FilterName) is null))
        {
            filters = filters.With(f.FilterName, f.DefaultValue);
        }

        var blocked = Restrict(spec, principal, filters);
        if (blocked is not null)
        {
            return blocked;
        }

        if (principal.TransporterId is { } own)
        {
            filters = filters.With(FilterNames.Transporter, null); // the company limit is applied to every fact; a transporter filter would only be ignored anyway
            _ = own;
        }

        var timer = Stopwatch.StartNew();
        var (providers, sources) = resolver.Resolve(settings);
        var kind = Periods.Parse(filters.Get(FilterNames.Period) ?? (filters.Get(FilterNames.FromDate) is not null || filters.Get(FilterNames.ToDate) is not null ? "custom" : null));
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime.AddMinutes(330));
        var range = Periods.Resolve(kind, today, filters.Date(FilterNames.FromDate), filters.Date(FilterNames.ToDate), settings.DefaultPeriodDays);
        var grain = filters.Get("grain") ?? "month";
        var compare = filters.Get(FilterNames.Compare) switch { "lastYear" => CompareMode.SamePeriodLastYear, "none" => CompareMode.None, _ => CompareMode.PreviousPeriod };
        var previous = Periods.Compare(range, CompareMode.PreviousPeriod, kind);
        var lastYear = Periods.Compare(range, CompareMode.SamePeriodLastYear, kind);

        var span = range;
        if (spec.Comparison)
        {
            span = Widen(Widen(span, previous), lastYear);
        }

        if (spec.TrendBuckets > 0)
        {
            span = Widen(span, new DateRange(ReportContext.BucketsFor(range.To, grain, spec.TrendBuckets)[0].Range.From, range.To));
        }

        if (spec.WideOnFilter is { } wide && filters.Get(wide) is not null)
        {
            span = new DateRange(today.AddYears(-3), today);
            range = span;
        }

        var cacheKey = CacheKey(definition, request, filters, principal, settings, range);
        var ttl = TimeToLive(spec, settings);
        if (!request.Refresh && ttl > 0 && cache.TryGetValue(cacheKey, out ReportResult? cached) && cached is not null)
        {
            var age = (int)(now - cached.GeneratedAtUtc).TotalSeconds;
            if (audit)
            {
                await auditor.WriteAsync(spec.Code, "Executed", Describe(filters, request), durationMs: 0, rows: cached.TotalRows, outcome: "cache", cancellationToken: cancellationToken);
            }

            return cached with { DataFreshnessSeconds = Math.Max(0, age), FromCache = true };
        }

        try
        {
            var facts = new ReportFacts(providers, range, filters, principal, settings, now, span);
            var unfiltered = principal.TransporterId is null && principal.Scopes.Count == 0 && filters.Values.Keys.All(k => k is FilterNames.Period or FilterNames.FromDate or FilterNames.ToDate or FilterNames.Compare or "grain");
            var context = new ReportContext(facts, kind, compare, grain) { Columns = definition.Columns.Select(c => c.FieldName).ToList(), Trends = unfiltered ? trends : null };
            var data = await spec.Build(context, cancellationToken);
            var result = Shape(spec, definition, data, request, filters, settings, range, previous, lastYear, kind, now, timer, sources, spec.Comparison, pageLimit ?? settings.MaxPageSize);

            if (result.IsFailure)
            {
                return result;
            }

            if (ttl > 0)
            {
                cache.Set(cacheKey, result.Value, TimeSpan.FromSeconds(ttl));
            }

            if (audit)
            {
                await auditor.WriteAsync(spec.Code, "Executed", Describe(filters, request), durationMs: result.Value.DurationMs, rows: result.Value.TotalRows, outcome: "ok", cancellationToken: cancellationToken);
            }

            if (result.Value.DurationMs > settings.SlowReportMs)
            {
                logger.LogWarning("Report {Report} was slow: {Duration} ms, {Rows} rows (threshold {Threshold} ms), trace {TraceId}", spec.Code, result.Value.DurationMs, result.Value.TotalRows, settings.SlowReportMs, System.Diagnostics.Activity.Current?.TraceId);
            }
            else
            {
                logger.LogInformation("Report {Report} ran in {Duration} ms, {Rows} rows, user {User}", spec.Code, result.Value.DurationMs, result.Value.TotalRows, principal.UserId);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Report {Report} failed for user {User}", spec.Code, principal.UserId);
            return Error.Failure("REPORT_EXECUTION_ERROR", "Unable to generate the requested report.");
        }
    }

    private static DateRange Widen(DateRange a, DateRange b) => new(a.From < b.From ? a.From : b.From, a.To > b.To ? a.To : b.To);

    private static int TimeToLive(ReportSpec spec, ReportSettings settings) => spec.Code switch
    {
        "R24_CONTROL_TOWER" => settings.ControlTowerCacheSeconds,
        _ when spec.Type == ReportType.Dashboard => settings.DashboardCacheSeconds,
        _ => 0,
    };

    private static string CacheKey(ReportDefinition d, ReportRequest r, ReportFilters f, ReportPrincipal p, ReportSettings s, DateRange range) =>
        $"rpt:run:{p.TenantId}:{d.Code}:{s.DataSource}:{p.ScopeKey}:{range.From:yyyyMMdd}-{range.To:yyyyMMdd}:{string.Join('&', f.Values.OrderBy(v => v.Key, StringComparer.Ordinal).Select(v => $"{v.Key}={v.Value}"))}:{string.Join(',', r.GroupBy ?? [])}:{string.Join(',', (r.Sort ?? []).Select(x => $"{x.Field}{x.Direction}"))}:{r.Page}:{r.PageSize}:{p.Permissions.Count}";

    /// <summary>Holds a limited user to the report: a user limited to customers or regions cannot see a report whose rows do not carry that, and an external user cannot ask about another company.</summary>
    private static Error? Restrict(ReportSpec spec, ReportPrincipal principal, ReportFilters filters)
    {
        foreach (var dimension in principal.Scopes.Where(s => s.Value.Count > 0).Select(s => s.Key))
        {
            if (!spec.ScopeDimensions.Contains(dimension, StringComparer.OrdinalIgnoreCase) && !CarriesByDefault(spec, dimension))
            {
                return Error.Forbidden("reports.scope_unsupported", $"This report cannot be limited to your {dimension} access, so it is not available to you.");
            }
        }

        if (principal.TransporterId is { } own && filters.Get(FilterNames.Transporter) is { } wanted && !wanted.Split(',', StringSplitOptions.TrimEntries).All(w => w.Equals(own.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            // A name cannot be checked without a lookup; an id for another company is answered as not found, never as forbidden.
            if (Guid.TryParse(wanted, out _))
            {
                return ReportAccess.NotFound;
            }
        }

        return null;
    }

    private static bool CarriesByDefault(ReportSpec spec, string dimension) => dimension switch
    {
        "customer" => spec.Columns.Any(c => c.Field == "customer"),
        "region" => spec.Columns.Any(c => c.Field is "region" or "lane"),
        "businessUnit" => false,
        _ => false,
    };

    private static string Describe(ReportFilters filters, ReportRequest request) =>
        JsonSerializer.Serialize(new { filters = filters.Values, groupBy = request.GroupBy, sort = request.Sort, page = request.Page, pageSize = request.PageSize }, JsonColumn.Options);

    private static Result<ReportResult> Shape(
        ReportSpec spec, ReportDefinition definition, ReportData data, ReportRequest request, ReportFilters filters, ReportSettings settings, DateRange range, DateRange previous, DateRange lastYear, PeriodKind kind,
        DateTimeOffset now, Stopwatch timer, IReadOnlyDictionary<string, string> sources, bool comparison, int pageLimit)
    {
        var rows = data.Rows;

        // Per-column text filters ("f_reason=damaged") and the free-text search over text columns.
        var columnFilters = filters.Values.Where(v => v.Key.StartsWith("f_", StringComparison.Ordinal)).ToDictionary(v => v.Key[2..], v => v.Value, StringComparer.OrdinalIgnoreCase);
        var unknown = columnFilters.Keys.FirstOrDefault(k => definition.Columns.All(c => !c.FieldName.Equals(k, StringComparison.OrdinalIgnoreCase)));
        if (unknown is not null)
        {
            return Error.Validation("reports.filter_invalid", $"'{unknown}' is not a column of this report.");
        }

        var shaped = ReportShaper.FilterColumns(rows, columnFilters);
        if (filters.Get(FilterNames.Search) is { } term && spec.Code != "R36_SHIPMENT_360")
        {
            var textColumns = definition.Columns.Where(c => c.DataType is FieldType.Text or FieldType.Status).Select(c => c.FieldName).ToList();
            shaped = shaped.Where(r => filters.MatchesSearch(textColumns.Select(c => r.GetValueOrDefault(c) is { } v ? Convert.ToString(v, CultureInfo.InvariantCulture) : null))).ToList();
            _ = term;
        }

        // Grouping.
        IReadOnlyList<string> groupBy;
        if (request.GroupBy is { } asked)
        {
            var bad = asked.FirstOrDefault(g => definition.Groupings.All(x => !x.FieldName.Equals(g, StringComparison.OrdinalIgnoreCase)));
            if (bad is not null)
            {
                return Error.Validation("reports.group_invalid", $"This report cannot be grouped by '{bad}'.");
            }

            groupBy = asked.Select(g => definition.Groupings.First(x => x.FieldName.Equals(g, StringComparison.OrdinalIgnoreCase)).FieldName).ToList();
        }
        else
        {
            groupBy = data.Fixed || spec.Measures.Count == 0 ? [] : spec.DefaultGroupBy;
        }

        var columns = definition.Columns.Where(c => c.Visible).OrderBy(c => c.Sequence).Select(c => new ColumnDto(c.FieldName, c.DisplayName, c.DataType.ToString(), c.Format, c.Sequence, c.Sortable, c.Filterable, c.Visible)).ToList();
        if (groupBy.Count > 0)
        {
            shaped = ReportShaper.Group(shaped, groupBy, spec.Measures);
            var measureFields = spec.Measures.Where(m => definition.Columns.Any(c => c.FieldName == m.Field && c.Visible)).Select(m => m.Field).ToHashSet(StringComparer.Ordinal);
            var dims = groupBy.Select((g, i) => new ColumnDto(g, definition.Groupings.First(x => x.FieldName == g).DisplayName, nameof(FieldType.Text), null, i + 1, true, false, true)).ToList();
            var measures = columns.Where(c => measureFields.Contains(c.Field)).Select((c, i) => c with { Sequence = dims.Count + i + 1, Sortable = true, Filterable = false }).ToList();
            columns = [.. dims, .. measures];
        }

        // Sorting: the request, else the definition's own order, else as built.
        var sorts = new List<(string Field, bool Descending)>();
        foreach (var s in request.Sort ?? [])
        {
            if (columns.All(c => !c.Field.Equals(s.Field, StringComparison.OrdinalIgnoreCase)))
            {
                return Error.Validation("reports.sort_invalid", $"This report cannot be sorted by '{s.Field}'.");
            }

            sorts.Add((columns.First(c => c.Field.Equals(s.Field, StringComparison.OrdinalIgnoreCase)).Field, string.Equals(s.Direction, "DESC", StringComparison.OrdinalIgnoreCase)));
        }

        if (sorts.Count == 0)
        {
            sorts.AddRange(definition.Sorts.OrderBy(s => s.Sequence).Where(s => columns.Any(c => c.Field == s.FieldName)).Select(s => (s.FieldName, s.Descending)));
        }

        if (sorts.Count == 0 && groupBy.Count > 0)
        {
            sorts.Add((groupBy[0], false));
        }

        shaped = ReportShaper.Sort(shaped, sorts);

        var pageSize = Math.Clamp(request.PageSize <= 0 ? 50 : request.PageSize, 1, pageLimit);
        var page = Math.Max(1, request.Page);
        var total = shaped.Count;
        var slice = shaped.Skip((page - 1) * pageSize).Take(pageSize).Cast<IReadOnlyDictionary<string, object?>>().ToList();

        var drills = spec.Drills.Select(d => new DrillDto(d.Field, d.TargetReport, d.Label, d.Map)).ToList();
        var summary = data.Totals.ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        var totals = data.Totals.Select(t => new TotalDto(t.Key, t.Label, t.Value, t.Unit, t.DrillReport, t.DrillFilters, t.Tone)).ToList();
        var applied = filters.Values.Where(v => !v.Key.StartsWith("f_", StringComparison.Ordinal)).ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase);
        timer.Stop();
        var period = new PeriodDto(range.From, range.To, comparison ? previous.From : null, comparison ? previous.To : null, comparison ? lastYear.From : null, comparison ? lastYear.To : null, kind.ToString());
        var mode = sources.Values.All(v => v.StartsWith("Demo", StringComparison.Ordinal)) ? "Demo" : sources.Values.Any(v => v.StartsWith("Demo", StringComparison.Ordinal)) ? "Mixed" : "Live";
        return new ReportResult(
            spec.Code, definition.Name, definition.Type.ToString(), now, 0, (int)timer.ElapsedMilliseconds, total, page, pageSize, columns, slice, summary, totals, data.Cards, data.Charts, data.Sections, data.Notes,
            applied, groupBy, drills, period, CalculationVersion, mode, definition.Refresh.ToString(), false);
    }

    /// <summary>All rows of a report as the user sees it (grouped, sorted), up to the export limit, with the columns the export should carry.</summary>
    public async Task<Result<ReportResult>> ExecuteForExportAsync(string reportCode, ReportRequest request, ReportPrincipal principal, int maxRows, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(reportCode, request with { Page = 1, PageSize = maxRows, Refresh = true }, principal, audit: false, cancellationToken, maxRows);
        return result;
    }
}
