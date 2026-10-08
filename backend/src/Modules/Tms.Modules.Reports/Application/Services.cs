using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Infrastructure.Persistence;
using Tms.Modules.Reports.Infrastructure.Providers;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Reports.Application;

/// <summary>Who may do what in Reports, in one place: the report's permission, the company limit of an external user and the rule that they see only vendor-safe reports.</summary>
internal static class ReportAccess
{
    public static readonly Error Forbidden = Error.Forbidden("reports.forbidden", "You are not allowed to open this report.");

    public static readonly Error NotFound = Error.NotFound("reports.not_found", "Report not found.");

    public static bool CanRun(ReportDefinition definition, ReportPrincipal principal) =>
        definition.Status == ReportStatus.Active
        && (principal.IsExternal
            ? definition.VendorSafe && principal.Has(ReportingPermissions.Self)
            : principal.Has(ReportingPermissions.Read) && principal.Has(definition.RequiredPermission));

    public static bool CanSee(ReportDefinition definition, ReportPrincipal principal) => CanRun(definition, principal);

    public static bool CanExport(ReportDefinition definition, ReportPrincipal principal) =>
        CanRun(definition, principal) && (principal.IsExternal || principal.Has(ReportingPermissions.Export));

    public static bool CanSchedule(ReportDefinition definition, ReportPrincipal principal) => !principal.IsExternal && CanRun(definition, principal) && principal.Has(ReportingPermissions.Schedule);
}

/// <summary>Each organisation's report settings: stored as JSON over built-in defaults, cached briefly.</summary>
internal sealed class ReportSettingsStore(ReportsDbContext db, IMemoryCache cache, ICurrentUser user)
{
    private static string Key(Guid tenant) => $"rpt:settings:{tenant}";

    public async Task<ReportSettings> GetAsync(CancellationToken cancellationToken)
    {
        var tenant = user.TenantId ?? Guid.Empty;
        if (cache.TryGetValue(Key(tenant), out ReportSettings? hit) && hit is not null)
        {
            return hit;
        }

        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var settings = Parse(row?.ValueJson);
        cache.Set(Key(tenant), settings, TimeSpan.FromSeconds(60));
        return settings;
    }

    public async Task<SettingsDto> GetDtoAsync(CancellationToken cancellationToken)
    {
        var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return new SettingsDto(Parse(row?.ValueJson), row?.Version ?? 0);
    }

    public static ReportSettings Parse(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new ReportSettings() : JsonSerializer.Deserialize<ReportSettings>(json, JsonColumn.Options) ?? new ReportSettings();

    public static string Serialise(ReportSettings settings) => JsonSerializer.Serialize(settings, JsonColumn.Options);

    public static Error? Validate(ReportSettings s)
    {
        if (s.AgeingBuckets.Length == 0 || s.AgeingBuckets.Any(b => b < 0) || s.AgeingBuckets.Zip(s.AgeingBuckets.Skip(1)).Any(p => p.First >= p.Second))
        {
            return Error.Validation("reports.settings_ageing", "Ageing buckets must be increasing numbers of days, for example 1, 3, 7, 15, 30.");
        }

        if (s.WorkingDays.Length == 0 || s.WorkingDays.Any(d => d is < 0 or > 6))
        {
            return Error.Validation("reports.settings_working_days", "Working days are 0 (Sunday) to 6 (Saturday), at least one.");
        }

        if (s.Holidays.Any(h => !DateOnly.TryParse(h, out _)))
        {
            return Error.Validation("reports.settings_holidays", "Holidays must be dates (yyyy-MM-dd).");
        }

        if (s.DataSource is not ("Live" or "Demo"))
        {
            return Error.Validation("reports.settings_source", "Data source must be Live or Demo.");
        }

        if (s.SyncExportRows is < 100 or > 100_000 || s.ExportMaxRows < s.SyncExportRows || s.MaxPageSize is < 10 or > 5_000 || s.EtaToleranceMinutes < 1 || s.JobKeepHours < 1 || s.TopN is < 3 or > 50)
        {
            return Error.Validation("reports.settings_limits", "One of the limits is out of range.");
        }

        return null;
    }

    public void Invalidate() => cache.Remove(Key(user.TenantId ?? Guid.Empty));
}

/// <summary>Chooses where facts come from: the modules' own providers, or the demonstration dataset when the organisation asks for it or a module is not installed.</summary>
internal sealed class ReportingProviderResolver(IServiceProvider services)
{
    public (ReportingProviders Providers, IReadOnlyDictionary<string, string> Sources) Resolve(ReportSettings settings)
    {
        var demo = services.GetRequiredService<DemoReportingData>();
        if (settings.DataSource == "Demo")
        {
            return (new ReportingProviders(demo, demo, demo, demo, demo), new Dictionary<string, string> { ["Planning"] = "Demo", ["Transporters"] = "Demo", ["Deliveries"] = "Demo", ["Tracking"] = "Demo", ["Freight Contracts"] = "Demo" });
        }

        var planning = services.GetService<IPlanningReportingProvider>();
        var transporters = services.GetService<ITransporterReportingProvider>();
        var pod = services.GetService<IPodReportingProvider>();
        var tracking = services.GetService<ITrackingReportingProvider>();
        var freight = services.GetService<IFreightContractReportingProvider>();
        string Source(object? live) => live is null or DemoReportingData ? "Demo (module not installed)" : "Live";
        return (new ReportingProviders(planning ?? demo, transporters ?? demo, pod ?? demo, tracking ?? demo, freight ?? demo),
            new Dictionary<string, string> { ["Planning"] = Source(planning), ["Transporters"] = Source(transporters), ["Deliveries"] = Source(pod), ["Tracking"] = Source(tracking), ["Freight Contracts"] = Source(freight) });
    }
}

/// <summary>Builds the caller's reporting authority, and freezes it into JSON for a job or subscription so a later run is held to the same limits.</summary>
internal sealed class PrincipalFactory(ReportsDbContext db, ICurrentUser user)
{
    private static readonly Dictionary<string, string> DimensionOf = new(StringComparer.OrdinalIgnoreCase) { ["Customer"] = "customer", ["Region"] = "region", ["BusinessUnit"] = "businessUnit" };

    public async Task<ReportPrincipal?> CurrentAsync(CancellationToken cancellationToken)
    {
        if (user.UserId is not { } userId || user.TenantId is not { } tenantId)
        {
            return null;
        }

        var scopes = await ScopesAsync(userId, cancellationToken);
        return new ReportPrincipal(userId, tenantId, user.Permissions, user.TransporterId, scopes);
    }

    public async Task<Dictionary<string, IReadOnlyList<string>>> ScopesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.DataScopes.AsNoTracking().Where(s => s.UserId == userId).ToListAsync(cancellationToken);
        return rows.GroupBy(r => DimensionOf.GetValueOrDefault(r.Dimension, r.Dimension.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Value).ToList(), StringComparer.OrdinalIgnoreCase);
    }

    public static string Freeze(ReportPrincipal p) => JsonSerializer.Serialize(new FrozenPrincipal(p.UserId, p.TenantId, p.Permissions.Order().ToList(), p.TransporterId, p.Scopes.ToDictionary(s => s.Key, s => s.Value.ToList())), JsonColumn.Options);

    public static ReportPrincipal Thaw(string json)
    {
        var f = JsonSerializer.Deserialize<FrozenPrincipal>(json, JsonColumn.Options)!;
        return new ReportPrincipal(f.UserId, f.TenantId, f.Permissions.ToHashSet(StringComparer.Ordinal), f.TransporterId, f.Scopes.ToDictionary(s => s.Key, s => (IReadOnlyList<string>)s.Value, StringComparer.OrdinalIgnoreCase));
    }

    private sealed record FrozenPrincipal(Guid UserId, Guid TenantId, List<string> Permissions, Guid? TransporterId, Dictionary<string, List<string>> Scopes);
}

/// <summary>The organisation's copy of the report and KPI catalogue. A new organisation gets the built-in set the first time it asks; later releases only add what is missing.</summary>
internal sealed class ReportDefinitionService(ReportsDbContext db, IMemoryCache cache, ICurrentUser user)
{
    private static string Key(Guid tenant) => $"rpt:defs:{tenant}";

    private static string SeededKey(Guid tenant) => $"rpt:seeded:{tenant}:{ReportCatalogue.All.Count}:{KpiCatalogue.All.Count}";

    public async Task<IReadOnlyList<ReportDefinition>> AllAsync(CancellationToken cancellationToken)
    {
        var tenant = user.TenantId ?? throw new InvalidOperationException("No tenant.");
        await EnsureSeededAsync(tenant, cancellationToken);
        if (cache.TryGetValue(Key(tenant), out IReadOnlyList<ReportDefinition>? hit) && hit is not null)
        {
            return hit;
        }

        var rows = await db.Definitions.AsNoTracking().AsSplitQuery().Include(d => d.Columns).Include(d => d.Filters).Include(d => d.Groupings).Include(d => d.Sorts).OrderBy(d => d.SortOrder).ToListAsync(cancellationToken);
        cache.Set(Key(tenant), (IReadOnlyList<ReportDefinition>)rows, TimeSpan.FromSeconds(120));
        return rows;
    }

    public async Task<ReportDefinition?> FindAsync(string code, CancellationToken cancellationToken) =>
        (await AllAsync(cancellationToken)).FirstOrDefault(d => d.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    public void Invalidate() => cache.Remove(Key(user.TenantId ?? Guid.Empty));

    public async Task<IReadOnlyList<KpiDefinitionRecord>> KpisAsync(CancellationToken cancellationToken)
    {
        var tenant = user.TenantId ?? throw new InvalidOperationException("No tenant.");
        await EnsureSeededAsync(tenant, cancellationToken);
        return await db.Kpis.AsNoTracking().OrderBy(k => k.ApplicableModule).ThenBy(k => k.KpiName).ToListAsync(cancellationToken);
    }

    private async Task EnsureSeededAsync(Guid tenant, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(SeededKey(tenant), out _))
        {
            return;
        }

        var existing = await db.Definitions.Include(d => d.Columns).Include(d => d.Filters).Include(d => d.Groupings).ToListAsync(cancellationToken);
        var changed = false;
        foreach (var spec in ReportCatalogue.All)
        {
            var summary = spec.Summary();
            var current = existing.FirstOrDefault(d => d.Code == spec.Code);
            if (current is null)
            {
                db.Definitions.Add(ReportDefinition.Create(tenant, summary));
                changed = true;
            }
            else if (current.Reconcile(summary))
            {
                changed = true;
            }
        }

        var kpis = await db.Kpis.Select(k => k.KpiCode).ToListAsync(cancellationToken);
        foreach (var kpi in KpiCatalogue.All.Where(k => !kpis.Contains(k.Code)))
        {
            db.Kpis.Add(KpiDefinitionRecord.Create(tenant, kpi));
            changed = true;
        }

        if (changed)
        {
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                // another request seeded at the same moment; what it wrote is what we need
                db.ChangeTracker.Clear();
            }

            cache.Remove(Key(tenant));
        }

        cache.Set(SeededKey(tenant), true, TimeSpan.FromMinutes(30));
    }
}

/// <summary>Writes the report audit trail: views, runs, exports, downloads and schedule changes.</summary>
internal sealed class ReportAuditor(ReportsDbContext db, ICurrentUser user, TimeProvider clock)
{
    public async Task WriteAsync(string reportCode, string action, string? parametersJson, string? exportFormat = null, int? durationMs = null, int? rows = null, string? outcome = null, Guid? by = null, CancellationToken cancellationToken = default)
    {
        if (user.TenantId is not { } tenant)
        {
            return;
        }

        db.AuditEntries.Add(ReportAuditEntry.Create(tenant, reportCode, action, by ?? user.UserId, parametersJson, clock.GetUtcNow(), exportFormat, durationMs, rows, outcome, user.TraceId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
