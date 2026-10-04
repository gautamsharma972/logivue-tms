using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Execution;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;

namespace LogiVue.Tms.TransporterManagement.Application.Performance;

/// <summary>
/// One KPI with its numerator and denominator kept side by side, so every percentage can be audited.
/// <see cref="Value"/> is null when there is nothing to measure (Not Measurable), never a silent zero.
/// </summary>
public sealed record OperationalKpi(KpiType Type, decimal Numerator, decimal Denominator)
{
    public decimal? Value => Denominator == 0 ? null : Math.Round(Numerator * 100m / Denominator, 2);
}

public sealed record OperationalMetrics(
    int DuePlacements,
    int PlacedOnTime,
    int Placed,
    int NoShows,
    int Replacements,
    double? AveragePlacementDelayMinutes,
    int MeasuredPickups,
    int NotMeasurablePickups,
    int LatePickupsCarrier,
    int LatePickupsNonCarrier,
    int LatePickupsUnattributed,
    double? AveragePickupDelayMinutes,
    int MeasuredDeliveries,
    int NotMeasurableDeliveries,
    int LateDeliveriesCarrier,
    int LateDeliveriesNonCarrier,
    int LateDeliveriesUnattributed,
    double? AverageDeliveryDelayMinutes,
    int PendingPod,
    double? AveragePodSubmissionHours);

public sealed record OperationalPeriodResult(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    IReadOnlyList<OperationalKpi> Kpis,
    OperationalMetrics Metrics);

/// <summary>Records gathered for one transporter and one period. <c>From</c> is inclusive and <c>To</c> exclusive.</summary>
public sealed record OperationalInputs(
    DateTime From,
    DateTime To,
    DateTime AsOf,
    int ToleranceMinutes,
    int GraceMinutes,
    IReadOnlyCollection<Tender> Invitations,
    IReadOnlyCollection<VehiclePlacementRequest> Placements,
    IReadOnlyCollection<LoadExecution> Executions,
    TransporterPodSummaryDto Pod,
    TransporterClaimsSummaryDto? Claims = null,
    TransporterCostSummaryDto? Cost = null,
    TransporterAvailabilitySummaryDto? Availability = null);

/// <summary>
/// Pure KPI rules for placement, pickup/delivery, POD and tender acceptance. No I/O, so every rule is unit-testable.
/// Rules that protect the transporter:
/// <list type="bullet">
/// <item>A delay counts against the transporter only when it is attributed to the carrier. Non-carrier and
/// unattributed delays are left out of both the numerator and the denominator.</item>
/// <item>Records with no planned time are Not Measurable and are excluded, not counted as failures.</item>
/// <item>A placement is only measured once it is due, so in-flight work is not penalised.</item>
/// <item>Claims, cost and availability are transporter-level. Their inputs are absent for lane and vehicle-type
/// groups, so those groups carry no value for them rather than a misleading zero.</item>
/// </list>
/// </summary>
public static class OperationalKpiCalculator
{
    /// <summary>KPI types this calculator owns. Recalculation replaces rows of these types and leaves others alone.</summary>
    public static readonly KpiType[] OwnedTypes =
    [
        KpiType.OnTimePickup, KpiType.OnTimeDelivery, KpiType.PlacementCompliance, KpiType.NoShowRate,
        KpiType.VehicleReplacementRate, KpiType.PodCompliance, KpiType.PodRejectionRate, KpiType.TenderAcceptance,
        KpiType.ClaimsRate, KpiType.CostPerformance, KpiType.Availability
    ];

    /// <summary>Version of these rules, stored on every KPI row so historical values stay explainable.</summary>
    public const int Version = 1;

    public static OperationalPeriodResult Calculate(OperationalInputs input, DateOnly periodStart, DateOnly periodEnd)
    {
        var kpis = new List<OperationalKpi>();

        // Tender acceptance: invitations sent in the period. Open invitations and those withdrawn by an award are excluded.
        var decided = input.Invitations
            .Where(t => t.SentAt is { } sent && sent >= input.From && sent < input.To)
            .Where(t => t.Status is TenderStatus.Accepted or TenderStatus.Awarded or TenderStatus.Rejected or TenderStatus.Expired)
            .ToList();
        kpis.Add(new OperationalKpi(KpiType.TenderAcceptance,
            decided.Count(t => t.Status is TenderStatus.Accepted or TenderStatus.Awarded), decided.Count));

        // Placement: placements that are due by the as-of time. A placement is compliant when it was placed within grace.
        var grace = TimeSpan.FromMinutes(input.GraceMinutes);
        var due = input.Placements
            .Where(p => p.Status != PlacementStatus.Cancelled)
            .Where(p => p.RequiredPlacementAt >= input.From && p.RequiredPlacementAt < input.To && p.RequiredPlacementAt <= input.AsOf)
            .ToList();
        var placed = due.Where(p => p.PlacedAt is not null).ToList();
        var placedOnTime = placed.Count(p => p.PlacedAt <= p.RequiredPlacementAt + grace);
        var noShows = due.Count(p => p.Status == PlacementStatus.NoShow);
        var replacements = due.Count(p => p.ReplacementCount > 0);

        kpis.Add(new OperationalKpi(KpiType.PlacementCompliance, placedOnTime, due.Count));
        kpis.Add(new OperationalKpi(KpiType.NoShowRate, noShows, due.Count));
        kpis.Add(new OperationalKpi(KpiType.VehicleReplacementRate, replacements, due.Count));

        // Claims, cost and availability. Each is Not Measurable (null value) when its source has nothing in the period.
        kpis.Add(new OperationalKpi(KpiType.ClaimsRate, input.Claims?.ClaimsCount ?? 0, input.Claims?.TotalShipments ?? 0));
        kpis.Add(new OperationalKpi(KpiType.CostPerformance, input.Cost?.OnBudgetLoads ?? 0, input.Cost?.LoadsWithCost ?? 0));
        kpis.Add(new OperationalKpi(KpiType.Availability, input.Availability?.VehicleDaysAvailable ?? 0, input.Availability?.VehicleDaysCommitted ?? 0));

        // Pickup and delivery: bucketed by the planned time. Only completed events are measured.
        var pickups = input.Executions
            .Where(e => e.PlannedPickupAt is { } p && p >= input.From && p < input.To && e.ActualPickupAt is not null)
            .ToList();
        var deliveries = input.Executions
            .Where(e => e.PlannedDeliveryAt is { } d && d >= input.From && d < input.To && e.ActualDeliveryAt is not null)
            .ToList();

        var pickupCarrierOk = pickups.Count(e => e.PickupAttribution == DelayAttribution.None);
        var pickupCarrierLate = pickups.Count(e => e.PickupAttribution == DelayAttribution.Carrier);
        kpis.Add(new OperationalKpi(KpiType.OnTimePickup, pickupCarrierOk, pickupCarrierOk + pickupCarrierLate));

        var deliveryCarrierOk = deliveries.Count(e => e.DeliveryAttribution == DelayAttribution.None);
        var deliveryCarrierLate = deliveries.Count(e => e.DeliveryAttribution == DelayAttribution.Carrier);
        kpis.Add(new OperationalKpi(KpiType.OnTimeDelivery, deliveryCarrierOk, deliveryCarrierOk + deliveryCarrierLate));

        // POD: the summary comes from the POD provider and is already limited to deliveries that required a POD.
        kpis.Add(new OperationalKpi(KpiType.PodCompliance, input.Pod.SubmittedWithinSla, input.Pod.DeliveriesRequiringPod));
        kpis.Add(new OperationalKpi(KpiType.PodRejectionRate, input.Pod.Rejected, input.Pod.Reviewed));

        var metrics = new OperationalMetrics(
            DuePlacements: due.Count,
            PlacedOnTime: placedOnTime,
            Placed: placed.Count,
            NoShows: noShows,
            Replacements: replacements,
            AveragePlacementDelayMinutes: Average(placed.Select(p => (p.PlacedAt!.Value - p.RequiredPlacementAt).TotalMinutes)),
            MeasuredPickups: pickups.Count,
            NotMeasurablePickups: input.Executions.Count(e => e.PlannedPickupAt is null && e.ActualPickupAt is { } a && a >= input.From && a < input.To),
            LatePickupsCarrier: pickupCarrierLate,
            LatePickupsNonCarrier: pickups.Count(e => e.PickupAttribution == DelayAttribution.NonCarrier),
            LatePickupsUnattributed: pickups.Count(e => e.PickupAttribution == DelayAttribution.Unattributed),
            AveragePickupDelayMinutes: Average(pickups.Select(e => (double)(e.PickupDelayMinutes ?? 0))),
            MeasuredDeliveries: deliveries.Count,
            NotMeasurableDeliveries: input.Executions.Count(e => e.PlannedDeliveryAt is null && e.ActualDeliveryAt is { } a && a >= input.From && a < input.To),
            LateDeliveriesCarrier: deliveryCarrierLate,
            LateDeliveriesNonCarrier: deliveries.Count(e => e.DeliveryAttribution == DelayAttribution.NonCarrier),
            LateDeliveriesUnattributed: deliveries.Count(e => e.DeliveryAttribution == DelayAttribution.Unattributed),
            AverageDeliveryDelayMinutes: Average(deliveries.Select(e => (double)(e.DeliveryDelayMinutes ?? 0))),
            PendingPod: input.Pod.Pending,
            AveragePodSubmissionHours: input.Pod.AverageSubmissionHours);

        return new OperationalPeriodResult(periodStart, periodEnd, kpis, metrics);
    }

    private static double? Average(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 1);
    }
}
