using Tms.Modules.Transporters.Domain;

namespace Tms.Modules.Transporters.Application.Performance;

/// <summary>One KPI with numerator and denominator side by side, so every percentage can be audited. <see cref="Value"/> is null when there is nothing to measure.</summary>
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

public sealed record OperationalPeriodResult(DateOnly PeriodStart, DateOnly PeriodEnd, IReadOnlyList<OperationalKpi> Kpis, OperationalMetrics Metrics);

/// <summary>A vehicle placement as the KPIs see it.</summary>
public sealed record PlacementInput(DateTimeOffset RequiredAt, DateTimeOffset? PlacedAt, bool NoShow, bool Cancelled, int ReplacementCount);

/// <param name="DeliveriesRequiringPod">Deliveries whose proof is due: submitted, or past the SLA. Deliveries still inside the SLA are left out.</param>
/// <param name="Rejected">Proofs refused at least once.</param>
/// <param name="Reviewed">Proofs with a final or rejected review outcome.</param>
public sealed record PodSummary(int DeliveriesRequiringPod, int SubmittedWithinSla, int Accepted, int Rejected, int Reviewed, int Pending, double? AverageSubmissionHours);

public sealed record ClaimsSummary(int TotalShipments, int ClaimsCount);

public sealed record CostSummary(int LoadsWithCost, int OnBudgetLoads);

/// <summary>Summed vehicle-days, so the ratio weights busy days correctly.</summary>
public sealed record AvailabilitySummary(int VehicleDaysCommitted, int VehicleDaysAvailable);

/// <summary>Records gathered for one transporter and one period. <c>From</c> is inclusive and <c>To</c> exclusive.</summary>
public sealed record OperationalInputs(
    DateTimeOffset From,
    DateTimeOffset To,
    DateTimeOffset AsOf,
    int GraceMinutes,
    IReadOnlyCollection<TenderInvitation> Invitations,
    IReadOnlyCollection<PlacementInput> Placements,
    IReadOnlyCollection<LoadExecution> Executions,
    PodSummary Pod,
    ClaimsSummary? Claims = null,
    CostSummary? Cost = null,
    AvailabilitySummary? Availability = null);

/// <summary>
/// Pure KPI rules for placement, pickup/delivery, POD and tender acceptance. No I/O, so every rule is unit-testable. Rules that protect the transporter:
/// a delay counts against it only when attributed to the carrier (non-carrier and unattributed delays leave both numerator and denominator);
/// records with no planned time are not measurable, not failures; a placement is measured only once due; claims, cost and availability are
/// transporter-level, so lane and vehicle-type groups carry no value for them rather than a misleading zero.
/// </summary>
public static class OperationalKpiCalculator
{
    /// <summary>KPI types this calculator owns. Recalculation replaces rows of these types.</summary>
    public static readonly KpiType[] OwnedTypes =
    [
        KpiType.OnTimePickup, KpiType.OnTimeDelivery, KpiType.PlacementCompliance, KpiType.NoShowRate, KpiType.VehicleReplacementRate,
        KpiType.PodCompliance, KpiType.PodRejectionRate, KpiType.TenderAcceptance, KpiType.ClaimsRate, KpiType.CostPerformance, KpiType.Availability,
    ];

    /// <summary>Version of these rules, stored on every KPI row so historical values stay explainable.</summary>
    public const int Version = 1;

    public static OperationalPeriodResult Calculate(OperationalInputs input, DateOnly periodStart, DateOnly periodEnd)
    {
        var kpis = new List<OperationalKpi>();

        // Tender acceptance: invitations sent in the period that were answered. Open ones are not yet a verdict either way.
        var decided = input.Invitations
            .Where(t => t.SentAt >= input.From && t.SentAt < input.To && t.Outcome != InvitationOutcome.Open)
            .ToList();
        kpis.Add(new OperationalKpi(KpiType.TenderAcceptance, decided.Count(t => t.Outcome == InvitationOutcome.Accepted), decided.Count));

        // Placement: placements due by the as-of time. Compliant when placed within the grace period.
        var grace = TimeSpan.FromMinutes(input.GraceMinutes);
        var due = input.Placements
            .Where(p => !p.Cancelled && p.RequiredAt >= input.From && p.RequiredAt < input.To && p.RequiredAt <= input.AsOf)
            .ToList();
        var placed = due.Where(p => p.PlacedAt is not null).ToList();
        var placedOnTime = placed.Count(p => p.PlacedAt <= p.RequiredAt + grace);
        var noShows = due.Count(p => p.NoShow);
        var replacements = due.Count(p => p.ReplacementCount > 0);

        kpis.Add(new OperationalKpi(KpiType.PlacementCompliance, placedOnTime, due.Count));
        kpis.Add(new OperationalKpi(KpiType.NoShowRate, noShows, due.Count));
        kpis.Add(new OperationalKpi(KpiType.VehicleReplacementRate, replacements, due.Count));

        // Claims, cost and availability: not measurable (null value) when their source has nothing in the period.
        kpis.Add(new OperationalKpi(KpiType.ClaimsRate, input.Claims?.ClaimsCount ?? 0, input.Claims?.TotalShipments ?? 0));
        kpis.Add(new OperationalKpi(KpiType.CostPerformance, input.Cost?.OnBudgetLoads ?? 0, input.Cost?.LoadsWithCost ?? 0));
        kpis.Add(new OperationalKpi(KpiType.Availability, input.Availability?.VehicleDaysAvailable ?? 0, input.Availability?.VehicleDaysCommitted ?? 0));

        // Pickup and delivery: bucketed by the planned time; only completed events are measured.
        var pickups = input.Executions.Where(e => e.PlannedPickupAt is { } p && p >= input.From && p < input.To && e.ActualPickupAt is not null).ToList();
        var deliveries = input.Executions.Where(e => e.PlannedDeliveryAt is { } d && d >= input.From && d < input.To && e.ActualDeliveryAt is not null).ToList();

        var pickupOk = pickups.Count(e => e.PickupAttribution == DelayAttribution.None);
        var pickupLate = pickups.Count(e => e.PickupAttribution == DelayAttribution.Carrier);
        kpis.Add(new OperationalKpi(KpiType.OnTimePickup, pickupOk, pickupOk + pickupLate));

        var deliveryOk = deliveries.Count(e => e.DeliveryAttribution == DelayAttribution.None);
        var deliveryLate = deliveries.Count(e => e.DeliveryAttribution == DelayAttribution.Carrier);
        kpis.Add(new OperationalKpi(KpiType.OnTimeDelivery, deliveryOk, deliveryOk + deliveryLate));

        kpis.Add(new OperationalKpi(KpiType.PodCompliance, input.Pod.SubmittedWithinSla, input.Pod.DeliveriesRequiringPod));
        kpis.Add(new OperationalKpi(KpiType.PodRejectionRate, input.Pod.Rejected, input.Pod.Reviewed));

        var metrics = new OperationalMetrics(
            DuePlacements: due.Count,
            PlacedOnTime: placedOnTime,
            Placed: placed.Count,
            NoShows: noShows,
            Replacements: replacements,
            AveragePlacementDelayMinutes: Average(placed.Select(p => (p.PlacedAt!.Value - p.RequiredAt).TotalMinutes)),
            MeasuredPickups: pickups.Count,
            NotMeasurablePickups: input.Executions.Count(e => e.PlannedPickupAt is null && e.ActualPickupAt is { } a && a >= input.From && a < input.To),
            LatePickupsCarrier: pickupLate,
            LatePickupsNonCarrier: pickups.Count(e => e.PickupAttribution == DelayAttribution.NonCarrier),
            LatePickupsUnattributed: pickups.Count(e => e.PickupAttribution == DelayAttribution.Unattributed),
            AveragePickupDelayMinutes: Average(pickups.Select(e => (double)(e.PickupDelayMinutes ?? 0))),
            MeasuredDeliveries: deliveries.Count,
            NotMeasurableDeliveries: input.Executions.Count(e => e.PlannedDeliveryAt is null && e.ActualDeliveryAt is { } a && a >= input.From && a < input.To),
            LateDeliveriesCarrier: deliveryLate,
            LateDeliveriesNonCarrier: deliveries.Count(e => e.DeliveryAttribution == DelayAttribution.NonCarrier),
            LateDeliveriesUnattributed: deliveries.Count(e => e.DeliveryAttribution == DelayAttribution.Unattributed),
            AverageDeliveryDelayMinutes: Average(deliveries.Select(e => (double)(e.DeliveryDelayMinutes ?? 0))),
            PendingPod: input.Pod.Pending,
            AveragePodSubmissionHours: input.Pod.AverageSubmissionHours);

        return new OperationalPeriodResult(periodStart, periodEnd, kpis, metrics);
    }

    /// <summary>Summarises delivered orders' proofs: due if submitted or past the SLA; on time if the first proof came within the SLA of delivery.</summary>
    public static PodSummary SummarisePod(IEnumerable<(DateTimeOffset DeliveredAt, DateTimeOffset? FirstProofAt, string Status, int Rejections)> deliveries, int slaHours, DateTimeOffset asOf)
    {
        var sla = TimeSpan.FromHours(slaHours);
        var due = deliveries.Where(d => d.FirstProofAt is not null || d.DeliveredAt + sla <= asOf).ToList();
        var submitted = due.Where(d => d.FirstProofAt is not null).ToList();
        var reviewed = due.Count(d => d.Status is "Verified" or "Rejected" || d.Rejections > 0);
        return new PodSummary(
            due.Count,
            submitted.Count(d => d.FirstProofAt <= d.DeliveredAt + sla),
            due.Count(d => d.Status == "Verified"),
            due.Count(d => d.Status == "Rejected" || d.Rejections > 0),
            reviewed,
            due.Count(d => d.FirstProofAt is null),
            Average(submitted.Select(d => (d.FirstProofAt!.Value - d.DeliveredAt).TotalHours)));
    }

    private static double? Average(IEnumerable<double> values)
    {
        var list = values.ToList();
        return list.Count == 0 ? null : Math.Round(list.Average(), 1);
    }
}
