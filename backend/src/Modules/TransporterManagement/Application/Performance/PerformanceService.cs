using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Execution;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Performance;

/// <summary>Request to rebuild a transporter's KPI buckets for a period. Both dates are inclusive.</summary>
public sealed record RecalculationRequest(long TransporterId, DateOnly From, DateOnly To);

/// <summary>
/// The KPI pipeline. KPIs are derived from operational records (placements, executions, POD outcomes, tender responses)
/// and never edited by hand. They are stored in calendar-month buckets, each with its numerator and denominator, for the
/// transporter as a whole, for each of its lanes, and for each vehicle type it has loads for.
/// </summary>
public interface IPerformanceService
{
    /// <summary>Calculates the period's transporter-level KPIs and metrics without storing them.</summary>
    Task<OperationalPeriodResult> GetOperationsAsync(long transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>Rebuilds every monthly bucket that overlaps the range. Use this to correct historical data.</summary>
    Task<IReadOnlyList<OperationalPeriodResult>> RecalculateAsync(long transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    /// <summary>
    /// Rebuilds the month buckets containing the given dates, as part of an operational event. The caller must have
    /// saved its own changes first, so the rebuild reads them. This method saves the KPI rows and nothing else.
    /// </summary>
    Task RefreshAsync(long transporterId, IEnumerable<DateTime> anchors, CancellationToken cancellationToken = default);
}

public sealed class PerformanceService(
    IRepository<Transporter> transporters,
    IRepository<Tender> tenders,
    IRepository<TransporterLane> lanes,
    IRepository<VehiclePlacementRequest> placements,
    IRepository<LoadExecution> executions,
    IRepository<PerformanceKpi> kpis,
    ITransporterPodProvider podProvider,
    ITransporterClaimsProvider claimsProvider,
    ITransporterCostProvider costProvider,
    ITransporterAvailabilityProvider availabilityProvider,
    ITransporterSettings settings,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    TimeProvider clock,
    ILogger<PerformanceService> logger) : IPerformanceService
{
    private static readonly KpiType[] PodTypes = [KpiType.PodCompliance, KpiType.PodRejectionRate];

    public async Task<OperationalPeriodResult> GetOperationsAsync(long transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        EnsureRange(from, to);
        await EnsureTransporterAsync(transporterId, cancellationToken);
        var data = await LoadAsync(transporterId, from, to, cancellationToken);
        return OperationalKpiCalculator.Calculate(ToInputs(data, data.Invitations, data.Placements, data.Executions, data.Pod, transporterLevel: true), from, to);
    }

    public async Task<IReadOnlyList<OperationalPeriodResult>> RecalculateAsync(long transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        EnsureRange(from, to);
        await EnsureTransporterAsync(transporterId, cancellationToken);

        var results = new List<OperationalPeriodResult>();
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        for (var month = StartOfMonth(from); month <= to; month = month.AddMonths(1))
        {
            results.Add(await StoreMonthAsync(transporterId, month, cancellationToken));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry("TransporterPerformance", transporterId.ToString(), "KpisRecalculated",
            NewValueJson: AuditJson.Serialize(new { from, to, months = results.Count })), cancellationToken);
        logger.LogInformation("Recalculated {Months} KPI month(s) for transporter {TransporterId}", results.Count, transporterId);
        return results;
    }

    public async Task RefreshAsync(long transporterId, IEnumerable<DateTime> anchors, CancellationToken cancellationToken = default)
    {
        var months = anchors.Select(a => StartOfMonth(DateOnly.FromDateTime(a))).Distinct().ToList();
        foreach (var month in months)
        {
            await StoreMonthAsync(transporterId, month, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Replaces the owned KPI rows of one calendar month for the transporter, its lanes and its vehicle types. Rows of
    /// other types, such as claims, are left alone.
    /// </summary>
    private async Task<OperationalPeriodResult> StoreMonthAsync(long transporterId, DateOnly month, CancellationToken cancellationToken)
    {
        var monthEnd = EndOfMonth(month);
        var data = await LoadAsync(transporterId, month, monthEnd, cancellationToken);
        var rows = new List<PerformanceKpi>();

        var whole = OperationalKpiCalculator.Calculate(ToInputs(data, data.Invitations, data.Placements, data.Executions, data.Pod, transporterLevel: true), month, monthEnd);
        rows.AddRange(ToRows(transporterId, whole.Kpis, month, monthEnd, laneId: null, vehicleTypeId: null, includePod: true));

        // Lane and vehicle-type breakdowns carry no POD figures: POD summaries are not split by lane or vehicle type.
        var noPod = EmptyPod(transporterId);
        foreach (var lane in data.Lanes)
        {
            bool OnLane(Tender t) => t.OriginLocationReference == lane.OriginLocationReference
                && t.DestinationLocationReference == lane.DestinationLocationReference && t.ServiceType == lane.ServiceType;
            var result = OperationalKpiCalculator.Calculate(ForGroup(data, OnLane, noPod), month, monthEnd);
            rows.AddRange(ToRows(transporterId, result.Kpis, month, monthEnd, laneId: lane.Id, vehicleTypeId: null, includePod: false));
        }

        foreach (var vehicleType in data.TenderByLoad.Values.Where(t => t.VehicleTypeReference is not null).Select(t => t.VehicleTypeReference!.Value).Distinct())
        {
            var result = OperationalKpiCalculator.Calculate(ForGroup(data, t => t.VehicleTypeReference == vehicleType, noPod), month, monthEnd);
            rows.AddRange(ToRows(transporterId, result.Kpis, month, monthEnd, laneId: null, vehicleTypeId: vehicleType, includePod: false));
        }

        var existing = (await kpis.ListAsync(k => k.TransporterId == transporterId, cancellationToken))
            .Where(k => k.PeriodStart == month && k.PeriodEnd == monthEnd && OperationalKpiCalculator.OwnedTypes.Contains(k.KpiType));
        foreach (var row in existing)
        {
            kpis.Remove(row);
        }

        foreach (var row in rows)
        {
            kpis.Add(row);
        }

        return whole;
    }

    private static List<PerformanceKpi> ToRows(long transporterId, IReadOnlyList<OperationalKpi> results, DateOnly month, DateOnly monthEnd,
        long? laneId, long? vehicleTypeId, bool includePod)
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

            rows.Add(new PerformanceKpi
            {
                TransporterId = transporterId,
                LaneReference = laneId,
                VehicleTypeReference = vehicleTypeId,
                KpiType = kpi.Type,
                PeriodStart = month,
                PeriodEnd = monthEnd,
                Numerator = kpi.Numerator,
                Denominator = kpi.Denominator,
                KpiValue = kpi.Value,
                CalculationVersion = OperationalKpiCalculator.Version
            });
        }

        return rows;
    }

    /// <summary>
    /// The records of one group, such as a lane or a vehicle type. A record belongs to a group through its load's tender.
    /// </summary>
    private static OperationalInputs ForGroup(MonthData data, Func<Tender, bool> belongs, TransporterPodSummaryDto pod)
    {
        var loads = data.TenderByLoad.Where(kv => belongs(kv.Value)).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
        return ToInputs(data,
            data.Invitations.Where(belongs).ToList(),
            data.Placements.Where(p => loads.Contains(p.LoadReference)).ToList(),
            data.Executions.Where(e => loads.Contains(e.LoadReference)).ToList(),
            pod,
            transporterLevel: false);
    }

    /// <summary>Transporter-level inputs also carry claims, cost and availability. Group inputs leave them out.</summary>
    private static OperationalInputs ToInputs(MonthData data, IReadOnlyCollection<Tender> invitations, IReadOnlyCollection<VehiclePlacementRequest> placements,
        IReadOnlyCollection<LoadExecution> executions, TransporterPodSummaryDto pod, bool transporterLevel) =>
        new(data.Start, data.End, data.AsOf, data.Tolerance, data.Grace, invitations, placements, executions, pod,
            transporterLevel ? data.Claims : null,
            transporterLevel ? data.Cost : null,
            transporterLevel ? data.Availability : null);

    private async Task<MonthData> LoadAsync(long transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var asOf = clock.GetUtcNow().UtcDateTime;

        var policy = await settings.GetAsync<DelayPolicySetting>(SettingKeys.ExecutionDelayPolicy, cancellationToken);
        var grace = await settings.GetAsync<int>(SettingKeys.PlacementGraceMinutes, cancellationToken);

        var invitations = await tenders.ListAsync(t => t.TransporterId == transporterId && t.SentAt != null && t.SentAt >= start && t.SentAt < end, cancellationToken);
        var due = await placements.ListAsync(p => p.TransporterId == transporterId && p.RequiredPlacementAt >= start && p.RequiredPlacementAt < end, cancellationToken);
        var load = await executions.ListAsync(e => e.TransporterId == transporterId
            && ((e.PlannedPickupAt >= start && e.PlannedPickupAt < end)
                || (e.PlannedDeliveryAt >= start && e.PlannedDeliveryAt < end)
                || (e.ActualPickupAt >= start && e.ActualPickupAt < end)
                || (e.ActualDeliveryAt >= start && e.ActualDeliveryAt < end)), cancellationToken);
        var pod = await podProvider.GetPodSummaryAsync(transporterId, start, end, cancellationToken);
        var claims = await claimsProvider.GetClaimsSummaryAsync(transporterId, start, end, cancellationToken);
        var cost = await costProvider.GetCostSummaryAsync(transporterId, start, end, cancellationToken);
        var availability = await availabilityProvider.GetAvailabilitySummaryAsync(transporterId, start, end, cancellationToken);

        // Group keys: each load's accepted or awarded tender gives its lane and vehicle type. Latest acceptance wins.
        var loadReferences = due.Select(p => p.LoadReference).Concat(load.Select(e => e.LoadReference)).ToHashSet(StringComparer.Ordinal);
        var tenderByLoad = (await tenders.ListAsync(t => t.TransporterId == transporterId
                && (t.Status == TenderStatus.Accepted || t.Status == TenderStatus.Awarded), cancellationToken))
            .Where(t => loadReferences.Contains(t.LoadReference))
            .OrderByDescending(t => t.Id)
            .GroupBy(t => t.LoadReference, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var laneRows = await lanes.ListAsync(l => l.TransporterId == transporterId, cancellationToken);

        return new MonthData(start, end, asOf, policy.ToleranceMinutes, grace, invitations, due, load, laneRows, tenderByLoad, pod, claims, cost, availability);
    }

    private sealed record MonthData(
        DateTime Start,
        DateTime End,
        DateTime AsOf,
        int Tolerance,
        int Grace,
        IReadOnlyList<Tender> Invitations,
        IReadOnlyList<VehiclePlacementRequest> Placements,
        IReadOnlyList<LoadExecution> Executions,
        IReadOnlyList<TransporterLane> Lanes,
        IReadOnlyDictionary<string, Tender> TenderByLoad,
        TransporterPodSummaryDto Pod,
        TransporterClaimsSummaryDto Claims,
        TransporterCostSummaryDto Cost,
        TransporterAvailabilitySummaryDto Availability);

    private static TransporterPodSummaryDto EmptyPod(long transporterId) =>
        new(transporterId, 0, 0, 0, 0, 0, 0, null);

    private async Task EnsureTransporterAsync(long transporterId, CancellationToken cancellationToken)
    {
        if (await transporters.FindAsync(transporterId, cancellationToken) is null)
        {
            throw new NotFoundException($"Transporter {transporterId} was not found.", "TRANSPORTER_NOT_FOUND");
        }
    }

    private static void EnsureRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new BusinessRuleException("The end of the period must be on or after its start.", "PERIOD_INVALID");
        }

        if (to.DayNumber - from.DayNumber > 366)
        {
            throw new BusinessRuleException("A recalculation covers at most one year.", "PERIOD_TOO_LONG");
        }
    }

    private static DateOnly StartOfMonth(DateOnly date) => new(date.Year, date.Month, 1);

    private static DateOnly EndOfMonth(DateOnly month) => month.AddMonths(1).AddDays(-1);
}
