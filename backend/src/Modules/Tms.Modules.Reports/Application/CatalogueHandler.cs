using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Infrastructure.Persistence;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Reports.Application;

/// <summary>The report catalogue a person can open: search, favourites, and each report's metadata so one screen can show any report.</summary>
internal sealed class CatalogueHandler(ReportDefinitionService definitions, PrincipalFactory principals, ReportsDbContext db, ReportAuditor auditor)
{
    public async Task<Result<IReadOnlyList<ReportSummaryDto>>> ListAsync(string? search, string? category, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        if (principal is null)
        {
            return ReportAccess.Forbidden;
        }

        var favourites = await FavouritesAsync(principal.UserId, cancellationToken);
        var all = await definitions.AllAsync(cancellationToken);
        var visible = all.Where(d => ReportAccess.CanSee(d, principal));
        if (!string.IsNullOrWhiteSpace(category))
        {
            visible = visible.Where(d => d.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var words = search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            visible = visible.Where(d => words.All(w => d.Name.Contains(w, StringComparison.OrdinalIgnoreCase) || d.Description.Contains(w, StringComparison.OrdinalIgnoreCase)
                || d.Category.Contains(w, StringComparison.OrdinalIgnoreCase) || d.Code.Contains(w, StringComparison.OrdinalIgnoreCase)));
        }

        return Result.Success<IReadOnlyList<ReportSummaryDto>>(visible.OrderBy(d => d.SortOrder).Select(d => Summary(d, principal, favourites)).ToList());
    }

    public async Task<Result<ReportMetadataDto>> MetadataAsync(string code, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        var definition = await definitions.FindAsync(code, cancellationToken);
        var spec = ReportCatalogue.Find(code);
        if (principal is null || definition is null || spec is null)
        {
            return ReportAccess.NotFound;
        }

        // A switched-off report can still be read (and switched on again) by whoever manages reports.
        var manager = definition.Status == ReportStatus.Disabled && !principal.IsExternal && principal.Has(ReportingPermissions.Manage);
        if (!ReportAccess.CanRun(definition, principal) && !manager)
        {
            return ReportAccess.Forbidden;
        }

        var favourites = await FavouritesAsync(principal.UserId, cancellationToken);
        await auditor.WriteAsync(definition.Code, "Viewed", null, cancellationToken: cancellationToken);
        var formats = Formats(definition);
        var drills = spec.Drills.Select(d => new DrillDto(d.Field, d.TargetReport, d.Label, d.Map)).ToList();
        return new ReportMetadataDto(
            definition.Code, definition.Name, definition.Description, definition.Category, definition.Type.ToString(), definition.DataSource, definition.Refresh.ToString(), definition.RequiredPermission, definition.Status.ToString(),
            definition.Columns.OrderBy(c => c.Sequence).Select(c => new ColumnDto(c.FieldName, c.DisplayName, c.DataType.ToString(), c.Format, c.Sequence, c.Sortable, c.Filterable, c.Visible)).ToList(),
            definition.Filters.OrderBy(f => f.Sequence).Select(f => new FilterDto(f.FilterName, f.DataType.ToString(), f.Required, f.DefaultValue, f.LookupSource, f.Sequence)).ToList(),
            definition.Groupings.OrderBy(g => g.Sequence).Select(g => new GroupingDto(g.FieldName, g.DisplayName)).ToList(),
            definition.Groupings.OrderBy(g => g.Sequence).Select(g => new GroupingDto(g.FieldName, g.DisplayName)).ToList(),
            spec.DefaultGroupBy, definition.Sorts.OrderBy(s => s.Sequence).Select(s => new SortDto(s.FieldName, s.Descending)).ToList(), drills,
            ReportAccess.CanExport(definition, principal) ? formats : [], ReportAccess.CanExport(definition, principal), ReportAccess.CanSchedule(definition, principal),
            favourites.Contains(definition.Code, StringComparer.OrdinalIgnoreCase), spec.Comparison, principal.IsExternal ? "Showing your company's figures only." : null, definition.Version);
    }

    public static IReadOnlyList<string> Formats(ReportDefinition d) => d.ExportFormats.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static ReportSummaryDto Summary(ReportDefinition d, ReportPrincipal principal, IReadOnlyList<string> favourites) =>
        new(d.Code, d.Name, d.Description, d.Category, d.Type.ToString(), d.DataSource, d.Refresh.ToString(), d.RequiredPermission, d.Status.ToString(), ReportAccess.CanRun(d, principal), d.VendorSafe, Formats(d),
            favourites.Contains(d.Code, StringComparer.OrdinalIgnoreCase));

    private async Task<IReadOnlyList<string>> FavouritesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var pref = await db.Preferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        return pref is null ? [] : JsonSerializer.Deserialize<List<string>>(pref.FavouritesJson, JsonColumn.Options) ?? [];
    }

    public async Task<Result<IReadOnlyList<KpiDefinitionDto>>> KpisAsync(CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        if (principal is null || !(principal.Has(ReportingPermissions.Read) || principal.Has(ReportingPermissions.Self)))
        {
            return ReportAccess.Forbidden;
        }

        var rows = await definitions.KpisAsync(cancellationToken);
        var visible = rows.Where(k => KpiCatalogue.Find(k.KpiCode) is { } kpi && KpiCalculationService.CanSee(kpi, principal));
        return Result.Success<IReadOnlyList<KpiDefinitionDto>>(visible.Select(Map).ToList());
    }

    public async Task<Result<KpiDefinitionDto>> KpiAsync(string code, CancellationToken cancellationToken)
    {
        var all = await KpisAsync(cancellationToken);
        if (all.IsFailure)
        {
            return all.Error;
        }

        return all.Value.FirstOrDefault(k => k.KpiCode.Equals(code, StringComparison.OrdinalIgnoreCase)) is { } found ? found : Error.NotFound("reports.kpi_not_found", "KPI not found.");
    }

    internal static KpiDefinitionDto Map(KpiDefinitionRecord k) =>
        new(k.KpiCode, k.KpiName, k.Description, k.FormulaDefinition, k.Unit, k.NumeratorDefinition, k.DenominatorDefinition, k.AggregationMethod, k.ApplicableModule, k.ApplicableServiceTypes, k.SourceOfTruth, k.CalculationVersion, k.HigherIsBetter, k.Status.ToString());
}

/// <summary>Run a report or a dashboard for the signed-in user.</summary>
internal sealed class ReportRunHandler(ReportExecutor executor)
{
    public Task<Result<ReportResult>> ExecuteAsync(string code, ReportRequest request, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(code, request, null, audit: true, cancellationToken);

    public Task<Result<ReportResult>> DashboardAsync(string code, Dictionary<string, JsonElement> filters, bool refresh, CancellationToken cancellationToken) =>
        executor.ExecuteAsync(code, new ReportRequest(code, filters, Refresh: refresh), null, audit: true, cancellationToken);
}

/// <summary>Values for filter dropdowns, read from the same facts the reports use and limited to what the caller may see.</summary>
internal sealed class LookupHandler(ReportSettingsStore settingsStore, ReportingProviderResolver resolver, PrincipalFactory principals, IMemoryCache cache, TimeProvider clock)
{
    private static readonly Dictionary<string, string[]> Fixed = new(StringComparer.OrdinalIgnoreCase)
    {
        ["service"] = ["FTL", "PTL", "Dedicated"],
        ["severity"] = ["Critical", "High", "Warning", "Info"],
        ["yesno"] = ["Yes", "No"],
        ["clock"] = ["Submission", "Review", "Resubmission"],
        ["rankBy"] = ["OverallScore", "OTP", "OTD", "Placement", "POD", "Claims", "Cost", "TenderAcceptance"],
        ["metric"] = ["OTD", "OTP", "Placement", "POD", "TenderAcceptance", "Claims", "Cost"],
        ["grain"] = ["day", "week", "month", "quarter", "year"],
        ["dimension"] = ["transporter", "lane", "vehicleType", "customer", "region"],
        ["period"] = ["Daily", "Weekly", "Monthly", "Quarterly", "Ytd", "Rolling30", "Rolling90", "Custom"],
        ["compare"] = ["none", "previous", "lastYear"],
        ["region"] = ["North", "South", "East", "West", "Central", "North-East"],
    };

    public async Task<Result<LookupDto>> GetAsync(string name, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        if (principal is null || !principal.Has(ReportingPermissions.Read) && !principal.Has(ReportingPermissions.Self))
        {
            return ReportAccess.Forbidden;
        }

        if (Fixed.TryGetValue(name, out var fixedValues))
        {
            return new LookupDto(name, fixedValues);
        }

        var key = $"rpt:lookup:{principal.TenantId}:{name}:{principal.ScopeKey}";
        if (cache.TryGetValue(key, out IReadOnlyList<string>? hit) && hit is not null)
        {
            return new LookupDto(name, hit);
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        var (providers, _) = resolver.Resolve(settings);
        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime.AddMinutes(330));
        var facts = new ReportFacts(providers, new DateRange(today.AddDays(-365), today.AddDays(30)), new ReportFilters(), principal, settings, now);
        var ships = await facts.Shipments();
        IEnumerable<string?> values = name.ToLowerInvariant() switch
        {
            "transporter" => ships.Select(s => s.TransporterName).Concat((await facts.Transporters()).Select(t => t.Name)),
            "lane" => ships.Select(s => s.Lane).Concat((await facts.Coverage()).Select(c => c.Lane)),
            "customer" => ships.Select(s => s.Customer),
            "vehicletype" => ships.Select(s => s.VehicleType),
            "city" => ships.SelectMany(s => new[] { s.OriginCity, s.DestinationCity }),
            "businessunit" => ships.Select(s => s.BusinessUnit),
            "zone" => (await facts.Rates()).Select(r => r.Zone),
            _ => [],
        };
        var list = values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToList();
        cache.Set(key, (IReadOnlyList<string>)list, TimeSpan.FromMinutes(5));
        return new LookupDto(name, list);
    }
}

/// <summary>Favourites, dashboard widgets, default filters and refresh interval: one person's own choices.</summary>
internal sealed class PreferenceHandler(ReportsDbContext db, ICurrentUser user)
{
    public async Task<Result<PreferenceDto>> GetAsync(CancellationToken cancellationToken)
    {
        if (user.UserId is not { } id)
        {
            return ReportAccess.Forbidden;
        }

        var p = await db.Preferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == id, cancellationToken);
        return Map(p);
    }

    public async Task<Result<PreferenceDto>> SaveAsync(SavePreferenceRequest request, CancellationToken cancellationToken)
    {
        if (user.UserId is not { } id || user.TenantId is not { } tenant)
        {
            return ReportAccess.Forbidden;
        }

        var valid = ReportCatalogue.All.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (request.Favourites?.FirstOrDefault(f => !valid.Contains(f)) is { } unknown)
        {
            return Error.Validation("reports.favourite_invalid", $"'{unknown}' is not a report.");
        }

        if (request.RefreshSeconds is { } r && r != 0 && (r < 15 || r > 3_600))
        {
            return Error.Validation("reports.refresh_invalid", "Refresh every 15 seconds to an hour, or 0 for never.");
        }

        var p = await db.Preferences.FirstOrDefaultAsync(x => x.UserId == id, cancellationToken);
        if (p is null)
        {
            p = UserReportPreference.Create(tenant, id);
            db.Preferences.Add(p);
        }

        p.Set(request.Favourites is null ? null : JsonSerializer.Serialize(request.Favourites.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), JsonColumn.Options),
            request.Widgets is null ? null : JsonSerializer.Serialize(request.Widgets, JsonColumn.Options),
            request.DefaultFilters is null ? null : JsonSerializer.Serialize(request.DefaultFilters, JsonColumn.Options), request.RefreshSeconds);
        await db.SaveChangesAsync(cancellationToken);
        return Map(p);
    }

    private static PreferenceDto Map(UserReportPreference? p) => p is null
        ? new PreferenceDto([], [], [], 0)
        : new PreferenceDto(JsonSerializer.Deserialize<List<string>>(p.FavouritesJson, JsonColumn.Options) ?? [], JsonSerializer.Deserialize<List<string>>(p.WidgetsJson, JsonColumn.Options) ?? [],
            JsonSerializer.Deserialize<Dictionary<string, string>>(p.DefaultFiltersJson, JsonColumn.Options) ?? [], p.RefreshSeconds);
}
