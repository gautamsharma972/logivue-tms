using Microsoft.Extensions.Logging;
using Tms.Modules.Reports.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Reports.Application;

/// <summary>The one way to ask for a KPI: by code, for a period and filters. Reports, dashboards and other modules all calculate through it.</summary>
public interface IKpiCalculationService
{
    Task<Result<KpiOutcome>> CalculateAsync(KpiRequest request, CancellationToken cancellationToken);
}

internal sealed class KpiCalculationService(
    ReportSettingsStore settingsStore,
    ReportingProviderResolver resolver,
    PrincipalFactory principals,
    ReportDefinitionService definitions,
    TimeProvider clock,
    ILogger<KpiCalculationService> logger) : IKpiCalculationService
{
    public async Task<Result<KpiOutcome>> CalculateAsync(KpiRequest request, CancellationToken cancellationToken)
    {
        var principal = await principals.CurrentAsync(cancellationToken);
        var kpi = KpiCatalogue.Find(request.Code);
        if (principal is null || !(principal.Has(ReportingPermissions.Read) || principal.Has(ReportingPermissions.Self)))
        {
            return ReportAccess.Forbidden;
        }

        if (kpi is null)
        {
            return Error.NotFound("reports.kpi_not_found", "KPI not found.");
        }

        var record = (await definitions.KpisAsync(cancellationToken)).FirstOrDefault(k => k.KpiCode == kpi.Code);
        if (record is { Status: ReportStatus.Disabled } || !CanSee(kpi, principal))
        {
            return Error.Forbidden("reports.kpi_forbidden", "You are not allowed to see this KPI.");
        }

        var settings = await settingsStore.GetAsync(cancellationToken);
        var (providers, _) = resolver.Resolve(settings);
        var filters = ReportFilters.FromJson(request.Filters);
        if (principal.TransporterId is not null)
        {
            filters = filters.With(FilterNames.Transporter, null);
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime.AddMinutes(330));
        var kind = Periods.Parse(request.Period ?? (filters.Get(FilterNames.FromDate) is not null ? "custom" : null));
        var range = Periods.Resolve(kind, today, filters.Date(FilterNames.FromDate), filters.Date(FilterNames.ToDate), settings.DefaultPeriodDays);
        var previous = Periods.Compare(range, CompareMode.PreviousPeriod, kind);
        var lastYear = Periods.Compare(range, CompareMode.SamePeriodLastYear, kind);
        var span = new DateRange(new[] { range.From, previous.From, lastYear.From }.Min(), new[] { range.To, previous.To, lastYear.To }.Max());
        var facts = new ReportFacts(providers, range, filters, principal, settings, now, span);
        try
        {
            var current = await KpiCatalogue.CalculateAsync(kpi, facts);
            var before = await KpiCatalogue.CalculateAsync(kpi, facts.ForPeriod(previous));
            var year = await KpiCatalogue.CalculateAsync(kpi, facts.ForPeriod(lastYear));
            decimal? change = current.Value is { } v && before.Value is { } p ? Math.Round(v - p, 2) : null;
            decimal? pct = change is { } c && before.Value is { } pv && pv != 0 && kpi.Unit != KpiUnit.Percent ? Math.Round(c / Math.Abs(pv) * 100m, 1) : null;
            return new KpiOutcome(Map(current), Map(before), Map(year), change, pct, KpiMath.Trend(change), filters.Values.ToDictionary(x => x.Key, x => x.Value));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "KPI {Kpi} failed", kpi.Code);
            return Error.Failure("REPORT_EXECUTION_ERROR", "Unable to calculate the requested KPI.");
        }
    }

    public static bool CanSee(KpiDefinition kpi, ReportPrincipal principal)
    {
        if (principal.IsExternal)
        {
            return principal.Has(ReportingPermissions.Self) && KpiCatalogue.IsVendorSafe(kpi.Code);
        }

        return !KpiCatalogue.IsCommercial(kpi.Code) || principal.Has(ReportingPermissions.Contracts) || principal.Has(ReportingPermissions.Executive);
    }

    private static KpiValueDto Map(KpiResult r) => new(r.Code, r.Name, r.Unit.ToString(), r.Value, r.Numerator, r.Denominator, r.Measurable, r.Excluded, r.Note, r.From, r.To, r.CalculationVersion);
}
