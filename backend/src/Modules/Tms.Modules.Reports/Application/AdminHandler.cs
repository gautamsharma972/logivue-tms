using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Reports.Application;

/// <summary>Administration of Reports: report and KPI definitions, settings, data scopes, the audit trail and which data the reports read.</summary>
internal sealed class AdminHandler(
    ReportsDbContext db, ReportDefinitionService definitions, ReportSettingsStore settingsStore, PrincipalFactory principals, ReportingProviderResolver resolver, IUserDirectory users, ReportAuditor auditor, ICurrentUser user,
    DailyKpiAggregator aggregator)
{
    public async Task<Result<int>> RebuildSummaryAsync(int days, CancellationToken cancellationToken)
    {
        if (await ManagerAsync(ReportingPermissions.Manage, cancellationToken) is not { } principal)
        {
            return ReportAccess.Forbidden;
        }

        var rows = await aggregator.RebuildAsync(principal.TenantId, principal.UserId, Math.Clamp(days, 1, 400), cancellationToken);
        await auditor.WriteAsync("SUMMARY", "SummaryRebuilt", null, rows: rows, cancellationToken: cancellationToken);
        return rows;
    }

    private async Task<ReportPrincipal?> ManagerAsync(string permission, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        return principal is { IsExternal: false } p && p.Has(permission) ? p : null;
    }

    public async Task<Result<ReportMetadataDto>> UpdateReportAsync(string code, UpdateReportRequest request, CancellationToken cancellationToken)
    {
        if (await ManagerAsync(ReportingPermissions.Manage, cancellationToken) is null)
        {
            return ReportAccess.Forbidden;
        }

        var definition = await db.Definitions.Include(d => d.Columns).FirstOrDefaultAsync(d => d.Code == code, cancellationToken);
        if (definition is null)
        {
            return ReportAccess.NotFound;
        }

        if (request.RequiredPermission is { } perm && ReportingPermissions.All.All(p => p.Code != perm))
        {
            return Error.Validation("reports.permission_unknown", $"'{perm}' is not a reports permission.");
        }

        if (request.Status is { } st && !Enum.TryParse<ReportStatus>(st, ignoreCase: true, out _))
        {
            return Error.Validation("reports.status_invalid", "Status is Active or Disabled.");
        }

        if (request.Name is { Length: > 160 })
        {
            return Error.Validation("reports.name_invalid", "The name can have up to 160 characters.");
        }

        db.Entry(definition).Property(d => d.Version).OriginalValue = request.Version;
        definition.Update(request.Name, request.Description, request.RequiredPermission, request.Status is null ? null : Enum.Parse<ReportStatus>(request.Status, ignoreCase: true));
        foreach (var c in request.Columns ?? [])
        {
            definition.SetColumn(c.Field, c.DisplayName, c.Visible, c.Sequence);
        }

        await db.SaveChangesAsync(cancellationToken);
        definitions.Invalidate();
        await auditor.WriteAsync(code, "DefinitionChanged", JsonSerializer.Serialize(request, JsonColumn.Options), cancellationToken: cancellationToken);
        return await MetadataAfterAsync(code, cancellationToken);
    }

    private async Task<Result<ReportMetadataDto>> MetadataAfterAsync(string code, CancellationToken cancellationToken)
    {
        var d = await definitions.FindAsync(code, cancellationToken);
        var spec = ReportCatalogue.Find(code);
        if (d is null || spec is null)
        {
            return ReportAccess.NotFound;
        }

        return new ReportMetadataDto(d.Code, d.Name, d.Description, d.Category, d.Type.ToString(), d.DataSource, d.Refresh.ToString(), d.RequiredPermission, d.Status.ToString(),
            d.Columns.OrderBy(c => c.Sequence).Select(c => new ColumnDto(c.FieldName, c.DisplayName, c.DataType.ToString(), c.Format, c.Sequence, c.Sortable, c.Filterable, c.Visible)).ToList(),
            d.Filters.OrderBy(f => f.Sequence).Select(f => new FilterDto(f.FilterName, f.DataType.ToString(), f.Required, f.DefaultValue, f.LookupSource, f.Sequence)).ToList(),
            d.Groupings.Select(g => new GroupingDto(g.FieldName, g.DisplayName)).ToList(), d.Groupings.Select(g => new GroupingDto(g.FieldName, g.DisplayName)).ToList(), spec.DefaultGroupBy,
            d.Sorts.Select(s => new SortDto(s.FieldName, s.Descending)).ToList(), spec.Drills.Select(x => new DrillDto(x.Field, x.TargetReport, x.Label, x.Map)).ToList(), CatalogueHandler.Formats(d), true, true, false, spec.Comparison, null, d.Version);
    }

    public async Task<Result<KpiDefinitionDto>> UpdateKpiAsync(string code, UpdateKpiRequest request, CancellationToken cancellationToken)
    {
        if (await ManagerAsync(ReportingPermissions.Manage, cancellationToken) is null)
        {
            return ReportAccess.Forbidden;
        }

        var kpi = await db.Kpis.FirstOrDefaultAsync(k => k.KpiCode == code, cancellationToken);
        if (kpi is null)
        {
            return Error.NotFound("reports.kpi_not_found", "KPI not found.");
        }

        db.Entry(kpi).Property(k => k.Version).OriginalValue = request.Version;
        kpi.Describe(request.Name, request.Description);
        if (request.Status is { } s)
        {
            if (!Enum.TryParse<ReportStatus>(s, ignoreCase: true, out var status))
            {
                return Error.Validation("reports.status_invalid", "Status is Active or Disabled.");
            }

            kpi.SetStatus(status);
        }

        await db.SaveChangesAsync(cancellationToken);
        await auditor.WriteAsync("KPI:" + code, "DefinitionChanged", JsonSerializer.Serialize(request, JsonColumn.Options), cancellationToken: cancellationToken);
        return CatalogueHandler.Map(kpi);
    }

    public async Task<Result<SettingsDto>> GetSettingsAsync(CancellationToken cancellationToken) =>
        await ManagerAsync(ReportingPermissions.Manage, cancellationToken) is null ? ReportAccess.Forbidden : await settingsStore.GetDtoAsync(cancellationToken);

    public async Task<Result<SettingsDto>> SaveSettingsAsync(SaveSettingsRequest request, CancellationToken cancellationToken)
    {
        if (await ManagerAsync(ReportingPermissions.Manage, cancellationToken) is not { } principal)
        {
            return ReportAccess.Forbidden;
        }

        if (ReportSettingsStore.Validate(request.Settings) is { } problem)
        {
            return problem;
        }

        var row = await db.Settings.FirstOrDefaultAsync(cancellationToken);
        var json = ReportSettingsStore.Serialise(request.Settings);
        var before = ReportSettingsStore.Parse(row?.ValueJson);
        if (row is null)
        {
            db.Settings.Add(ReportSetting.Create(principal.TenantId, json));
        }
        else
        {
            db.Entry(row).Property(r => r.Version).OriginalValue = request.Version;
            row.Set(json);
        }

        await db.SaveChangesAsync(cancellationToken);
        if (before.DataSource != request.Settings.DataSource || before.RegionMap.Count != request.Settings.RegionMap.Count)
        {
            // Kept daily KPI values were worked out from the other data (or the other regions): they must not be read as if they were these.
            await db.DailyKpis.ExecuteDeleteAsync(cancellationToken);
        }

        settingsStore.Invalidate();
        await auditor.WriteAsync("SETTINGS", "SettingsChanged", json, cancellationToken: cancellationToken);
        return await settingsStore.GetDtoAsync(cancellationToken);
    }

    public async Task<Result<DataSourceStatusDto>> DataSourceAsync(CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        if (principal is null || !principal.Has(ReportingPermissions.Read) && !principal.Has(ReportingPermissions.Self))
        {
            return ReportAccess.Forbidden;
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        var (_, sources) = resolver.Resolve(settings);
        return new DataSourceStatusDto(settings.DataSource, sources, true);
    }

    public async Task<Result<IReadOnlyList<DataScopeDto>>> ScopesAsync(Guid? userId, CancellationToken cancellationToken)
    {
        if (await ManagerAsync(ReportingPermissions.Manage, cancellationToken) is null)
        {
            return ReportAccess.Forbidden;
        }

        var rows = await db.DataScopes.AsNoTracking().Where(s => userId == null || s.UserId == userId).ToListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<DataScopeDto>>(rows.GroupBy(r => (r.UserId, r.Dimension)).Select(g => new DataScopeDto(g.Key.UserId, g.Key.Dimension, g.Select(x => x.Value).Order().ToList())).ToList());
    }

    public async Task<Result<DataScopeDto>> SaveScopeAsync(SaveDataScopeRequest request, CancellationToken cancellationToken)
    {
        if (await ManagerAsync(ReportingPermissions.Manage, cancellationToken) is not { } principal)
        {
            return ReportAccess.Forbidden;
        }

        if (request.Dimension is not ("Customer" or "Region" or "BusinessUnit"))
        {
            return Error.Validation("reports.scope_dimension", "A scope limits Customer, Region or BusinessUnit.");
        }

        var values = request.Values.Select(v => v.Trim()).Where(v => v.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (values.Any(v => v.Length > 200) || values.Count > 200)
        {
            return Error.Validation("reports.scope_values", "Give up to 200 values of up to 200 characters.");
        }

        var existing = await db.DataScopes.Where(s => s.UserId == request.UserId && s.Dimension == request.Dimension).ToListAsync(cancellationToken);
        db.DataScopes.RemoveRange(existing);
        foreach (var v in values)
        {
            db.DataScopes.Add(DataScope.Create(principal.TenantId, request.UserId, request.Dimension, v));
        }

        await db.SaveChangesAsync(cancellationToken);
        await auditor.WriteAsync("DATA_SCOPE", "ScopeChanged", JsonSerializer.Serialize(request, JsonColumn.Options), outcome: request.UserId.ToString(), cancellationToken: cancellationToken);
        return new DataScopeDto(request.UserId, request.Dimension, values);
    }

    public async Task<Result<PagedResult<AuditEntryDto>>> AuditAsync(string? report, string? action, Guid? by, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (await ManagerAsync(ReportingPermissions.Audit, cancellationToken) is null)
        {
            return ReportAccess.Forbidden;
        }

        var query = db.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(report))
        {
            query = query.Where(a => a.ReportCode == report);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        if (by is { } u)
        {
            query = query.Where(a => a.RequestedBy == u);
        }

        if (from is { } f)
        {
            var start = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
            query = query.Where(a => a.PerformedAtUtc >= start);
        }

        if (to is { } t)
        {
            var end = new DateTimeOffset(t.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
            query = query.Where(a => a.PerformedAtUtc < end);
        }

        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(1, page);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(a => a.PerformedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        var names = await users.GetDisplayNamesAsync(rows.Where(r => r.RequestedBy is not null).Select(r => r.RequestedBy!.Value).Distinct(), cancellationToken);
        _ = user;
        return new PagedResult<AuditEntryDto>(rows.Select(r => new AuditEntryDto(r.Id, r.ReportCode, r.Action, r.RequestedBy, r.RequestedBy is { } id ? names.GetValueOrDefault(id) : null, r.ParametersJson, r.PerformedAtUtc, r.ExportFormat, r.DurationMs, r.RowCount, r.Outcome)).ToList(), page, pageSize, total);
    }
}
