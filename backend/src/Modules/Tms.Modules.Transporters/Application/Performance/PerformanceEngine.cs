using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Application.Performance;

/// <summary>
/// The KPI pipeline. KPIs are derived from operational records (tender responses, executions, proofs of delivery, placements, claims, cost,
/// capacity) and never edited by hand. They are stored in calendar-month buckets, each with its numerator and denominator, for the transporter as
/// a whole, for each of its lanes and for each vehicle type it has loads for.
/// </summary>
internal sealed class PerformanceEngine(
    TransportersDbContext db, IShipmentOperationsFeed feed, ITransporterSettings settings, TimeProvider clock, ILogger<PerformanceEngine> logger)
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);
    private static readonly KpiType[] PodTypes = [KpiType.PodCompliance, KpiType.PodRejectionRate];

    public static DateTimeOffset StartOf(DateOnly date) => new(date.Year, date.Month, date.Day, 0, 0, 0, India);

    public static DateOnly StartOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    public static DateOnly EndOfMonth(DateOnly month) => month.AddMonths(1).AddDays(-1);

    public static Error? CheckRange(DateOnly from, DateOnly to) =>
        to < from ? Error.Validation("performance.period_invalid", "The end of the period must be on or after its start.")
        : to.DayNumber - from.DayNumber > 366 ? Error.Validation("performance.period_too_long", "A period covers at most one year.")
        : null;

    /// <summary>Calculates the period's transporter-level KPIs and metrics without storing them.</summary>
    public async Task<OperationalPeriodResult> OperationsAsync(Guid transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var data = await LoadAsync(transporterId, from, to, cancellationToken);
        return OperationalKpiCalculator.Calculate(ToInputs(data, data.Invitations, data.Executions, data.Pod, transporterLevel: true), from, to);
    }

    /// <summary>Rebuilds every monthly bucket that overlaps the range. Use this to correct historical data.</summary>
    public async Task<IReadOnlyList<OperationalPeriodResult>> RecalculateAsync(Guid transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var results = new List<OperationalPeriodResult>();
        for (var month = StartOfMonth(from); month <= to; month = month.AddMonths(1))
        {
            results.Add(await StoreMonthAsync(transporterId, month, cancellationToken));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Recalculated {Months} KPI month(s) for transporter {TransporterId}", results.Count, transporterId);
        return results;
    }

    /// <summary>Rebuilds the month buckets containing the given moments, as part of an operational event.</summary>
    public async Task RefreshAsync(Guid transporterId, IEnumerable<DateTimeOffset> anchors, CancellationToken cancellationToken)
    {
        foreach (var month in anchors.Select(a => StartOfMonth(DateOnly.FromDateTime(a.ToOffset(India).DateTime))).Distinct())
        {
            await StoreMonthAsync(transporterId, month, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<OperationalPeriodResult> StoreMonthAsync(Guid transporterId, DateOnly month, CancellationToken cancellationToken)
    {
        var monthEnd = EndOfMonth(month);
        var data = await LoadAsync(transporterId, month, monthEnd, cancellationToken);
        var tenantId = data.TenantId;
        var rows = new List<PerformanceKpi>();

        var whole = OperationalKpiCalculator.Calculate(ToInputs(data, data.Invitations, data.Executions, data.Pod, transporterLevel: true), month, monthEnd);
        rows.AddRange(ToRows(tenantId, transporterId, whole.Kpis, month, monthEnd, laneId: null, vehicleTypeId: null, includePod: true));

        // Lane and vehicle-type breakdowns carry no POD, claims, cost or availability figures: those are not split by lane or vehicle type.
        var noPod = new PodSummary(0, 0, 0, 0, 0, 0, null);
        foreach (var lane in data.Lanes)
        {
            bool OnLaneInvite(TenderInvitation t) => t.OriginState is not null && lane.Covers(t.OriginState, t.OriginCity ?? string.Empty, t.DestinationState, t.DestinationCity, t.Mode ?? FreightMode.Ftl);
            bool OnLaneExec(LoadExecution e) => e.OriginState is not null && lane.Covers(e.OriginState, e.OriginCity ?? string.Empty, e.DestinationState, e.DestinationCity, e.Mode);
            var result = OperationalKpiCalculator.Calculate(
                ToInputs(data, data.Invitations.Where(OnLaneInvite).ToList(), data.Executions.Where(OnLaneExec).ToList(), noPod, transporterLevel: false), month, monthEnd);
            rows.AddRange(ToRows(tenantId, transporterId, result.Kpis, month, monthEnd, laneId: lane.Id, vehicleTypeId: null, includePod: false));
        }

        var vehicleTypes = data.Invitations.Where(t => t.VehicleTypeId is not null).Select(t => t.VehicleTypeId!.Value)
            .Concat(data.Executions.Where(e => e.VehicleTypeId is not null).Select(e => e.VehicleTypeId!.Value)).Distinct().ToList();
        foreach (var vehicleType in vehicleTypes)
        {
            var result = OperationalKpiCalculator.Calculate(
                ToInputs(data, data.Invitations.Where(t => t.VehicleTypeId == vehicleType).ToList(), data.Executions.Where(e => e.VehicleTypeId == vehicleType).ToList(), noPod, transporterLevel: false),
                month, monthEnd);
            rows.AddRange(ToRows(tenantId, transporterId, result.Kpis, month, monthEnd, laneId: null, vehicleTypeId: vehicleType, includePod: false));
        }

        var owned = OperationalKpiCalculator.OwnedTypes;
        var existing = await db.Kpis.Where(k => k.TransporterId == transporterId && k.PeriodStart == month && k.PeriodEnd == monthEnd && owned.Contains(k.KpiType)).ToListAsync(cancellationToken);
        db.Kpis.RemoveRange(existing);
        db.Kpis.AddRange(rows);
        return whole;
    }

    private static List<PerformanceKpi> ToRows(
        Guid tenantId, Guid transporterId, IReadOnlyList<OperationalKpi> results, DateOnly month, DateOnly monthEnd, Guid? laneId, Guid? vehicleTypeId, bool includePod)
    {
        var rows = new List<PerformanceKpi>();
        foreach (var kpi in results)
        {
            if (!includePod && PodTypes.Contains(kpi.Type))
            {
                continue;
            }

            // Lane and vehicle-type rows are stored only when they have something to measure.
            if ((laneId is not null || vehicleTypeId is not null) && kpi.Denominator == 0)
            {
                continue;
            }

            rows.Add(PerformanceKpi.Create(tenantId, transporterId, laneId, vehicleTypeId, kpi.Type, month, monthEnd, kpi.Numerator, kpi.Denominator, kpi.Value, OperationalKpiCalculator.Version));
        }

        return rows;
    }

    private static OperationalInputs ToInputs(
        MonthData data, IReadOnlyCollection<TenderInvitation> invitations, IReadOnlyCollection<LoadExecution> executions, PodSummary pod, bool transporterLevel) =>
        new(data.Start, data.End, data.AsOf, data.Grace, invitations, data.Placements, executions, pod,
            transporterLevel ? data.Claims : null, transporterLevel ? data.Cost : null, transporterLevel ? data.Availability : null);

    private async Task<MonthData> LoadAsync(Guid transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var start = StartOf(from);
        var end = StartOf(to.AddDays(1));
        var asOf = clock.GetUtcNow();
        var grace = await settings.GetAsync<int>(SettingKeys.PlacementGraceMinutes, cancellationToken);
        var sla = await settings.GetAsync<int>(SettingKeys.PodSubmissionSlaHours, cancellationToken);

        var invitations = await db.Invitations.AsNoTracking().Where(t => t.TransporterId == transporterId && t.SentAt >= start && t.SentAt < end).ToListAsync(cancellationToken);
        var executions = await db.Executions.AsNoTracking().Where(e => e.TransporterId == transporterId
            && ((e.PlannedPickupAt >= start && e.PlannedPickupAt < end) || (e.PlannedDeliveryAt >= start && e.PlannedDeliveryAt < end)
                || (e.ActualPickupAt >= start && e.ActualPickupAt < end) || (e.ActualDeliveryAt >= start && e.ActualDeliveryAt < end))).ToListAsync(cancellationToken);
        var lanes = await db.Lanes.AsNoTracking().Where(l => l.TransporterId == transporterId && l.IsActive).ToListAsync(cancellationToken);

        var facts = await feed.ListAsync(transporterId, start, end, cancellationToken);
        var delivered = facts.SelectMany(f => f.Deliveries).Where(d => d.DeliveredAt >= start && d.DeliveredAt < end)
            .Select(d => (d.DeliveredAt, d.FirstProofAt, d.ProofStatus, Rejections: d.ProofRejections)).ToList();
        var pod = OperationalKpiCalculator.SummarisePod(delivered, sla, asOf);

        var tenantId = await db.Transporters.AsNoTracking().Where(t => t.Id == transporterId).Select(t => t.TenantId).FirstOrDefaultAsync(cancellationToken);
        return new MonthData(tenantId, start, end, asOf, grace, invitations, [], executions, lanes, pod, null, null, null);
    }

    private sealed record MonthData(
        Guid TenantId,
        DateTimeOffset Start,
        DateTimeOffset End,
        DateTimeOffset AsOf,
        int Grace,
        IReadOnlyList<TenderInvitation> Invitations,
        IReadOnlyList<PlacementInput> Placements,
        IReadOnlyList<LoadExecution> Executions,
        IReadOnlyList<TransporterLane> Lanes,
        PodSummary Pod,
        ClaimsSummary? Claims,
        CostSummary? Cost,
        AvailabilitySummary? Availability);
}
