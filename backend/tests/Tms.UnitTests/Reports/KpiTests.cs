using Tms.Modules.Reports.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Reports;

/// <summary>Every KPI against facts whose answer is worked out by hand: the numerator and denominator, not just the percentage.</summary>
public class KpiTests
{
    private static ReportFacts Facts(TestProvider p, ReportPrincipal? principal = null, ReportFilters? filters = null, ReportSettings? settings = null) =>
        new(F.All(p), new DateRange(F.Day.AddDays(-3), F.Day.AddDays(3)), filters ?? new ReportFilters(), principal ?? ReportTestKit.Admin(), settings ?? new ReportSettings(), ReportTestKit.Now);

    private static Task<KpiResult> Calc(string code, TestProvider p, ReportFacts? facts = null) => KpiCatalogue.CalculateAsync(KpiCatalogue.Find(code)!, facts ?? Facts(p));

    [Fact]
    public async Task OTP_counts_pickups_on_time_over_pickups_with_a_planned_and_an_actual_time_and_leaves_the_rest_out()
    {
        var p = new TestProvider();
        var planned = F.Noon;
        p.Shipments.Add(F.Ship("S1", plannedPickup: planned, actualPickup: planned.AddMinutes(-5)));
        p.Shipments.Add(F.Ship("S2", plannedPickup: planned, actualPickup: planned.AddMinutes(40)));
        p.Shipments.Add(F.Ship("S3", plannedPickup: planned, actualPickup: planned));
        p.Shipments.Add(F.Ship("S4", plannedPickup: null, actualPickup: planned)); // no planned time: not measurable, not late

        var r = await Calc("OTP", p);

        r.Numerator.ShouldBe(2m);
        r.Denominator.ShouldBe(3m);
        r.Value.ShouldBe(66.7m);
        r.Excluded.ShouldBe(1);
    }

    [Fact]
    public async Task OTP_allows_the_grace_the_organisation_sets()
    {
        var p = new TestProvider();
        p.Shipments.Add(F.Ship("S1", plannedPickup: F.Noon, actualPickup: F.Noon.AddMinutes(20)));
        (await Calc("OTP", p)).Value.ShouldBe(0m);
        (await Calc("OTP", p, Facts(p, settings: new ReportSettings { OtpGraceMinutes = 30 }))).Value.ShouldBe(100m);
    }

    [Fact]
    public async Task OTD_uses_the_deliveries_own_judgement_and_a_delivery_with_no_planned_time_is_not_measurable_not_zero()
    {
        var p = new TestProvider();
        p.Deliveries.Add(F.Delivery("D1", onTime: true));
        p.Deliveries.Add(F.Delivery("D2", onTime: true));
        p.Deliveries.Add(F.Delivery("D3", onTime: false));
        p.Deliveries.Add(F.Delivery("D4", onTime: null));
        p.Deliveries.Add(F.Delivery("D5", onTime: null, status: "Failed"));

        var r = await Calc("OTD", p);

        (r.Numerator, r.Denominator).ShouldBe((2m, 3m));
        r.Value.ShouldBe(66.7m);
        r.Excluded.ShouldBe(1, "the delivery with no planned time; a failed delivery is not a delivery to judge");

        var none = new TestProvider();
        none.Deliveries.Add(F.Delivery("D9", onTime: null));
        var empty = await Calc("OTD", none);
        empty.Value.ShouldBeNull("nothing could be judged, so the answer is not 0%");
        empty.Measurable.ShouldBeFalse();
        empty.Note!.ShouldContain("Not measurable");
    }

    [Fact]
    public async Task POD_compliance_leaves_out_proofs_not_required_and_proofs_not_yet_due()
    {
        var p = new TestProvider();
        p.Pods.Add(F.Pod("P1", withinSla: true));
        p.Pods.Add(F.Pod("P2", withinSla: true));
        p.Pods.Add(F.Pod("P3", withinSla: false));
        p.Pods.Add(F.Pod("P4", required: false, withinSla: null)); // not required: N/A
        p.Pods.Add(F.Pod("P5", withinSla: null, status: "Pending")); // inside its SLA: neither compliant nor late

        var r = await Calc("POD_COMPLIANCE", p);

        (r.Numerator, r.Denominator).ShouldBe((2m, 3m));
        r.Excluded.ShouldBe(1);
        r.Value.ShouldBe(66.7m);
    }

    [Fact]
    public async Task Placement_compliance_counts_late_no_show_and_replaced_against_the_carrier()
    {
        var p = new TestProvider();
        foreach (var o in new[] { "OnTime", "OnTime", "OnTime", "Late", "NoShow", "Replaced" })
        {
            p.Placements.Add(F.Placement(o));
        }

        var r = await Calc("PLACEMENT_COMPLIANCE", p);

        (r.Numerator, r.Denominator).ShouldBe((3m, 6m));
        r.Value.ShouldBe(50m);
    }

    [Fact]
    public async Task Tender_acceptance_is_accepted_over_accepted_rejected_and_expired_while_open_and_withdrawn_offers_are_not_counted()
    {
        var p = new TestProvider();
        foreach (var o in new[] { "Accepted", "Accepted", "Accepted", "Rejected", "Expired", "Pending", "Withdrawn" })
        {
            p.Tenders.Add(F.Tender(o));
        }

        var r = await Calc("TENDER_ACCEPTANCE", p);

        (r.Numerator, r.Denominator).ShouldBe((3m, 5m));
        r.Value.ShouldBe(60m);
        r.Excluded.ShouldBe(2);
    }

    [Fact]
    public async Task Weight_and_volume_utilisation_are_ratios_of_sums_not_averages_of_percentages()
    {
        var p = new TestProvider();
        p.Vehicles.Add(F.Plan(kg: 16_000, capacityKg: 16_000, cbm: 60, capacityCbm: 60)); // 100%
        p.Vehicles.Add(F.Plan(kg: 1_000, capacityKg: 4_000, cbm: 5, capacityCbm: 20)); // 25%

        var w = await Calc("WEIGHT_UTIL", p);
        var v = await Calc("VOLUME_UTIL", p);

        (w.Numerator, w.Denominator).ShouldBe((17_000m, 20_000m));
        w.Value.ShouldBe(85m, "a plain average of 100% and 25% would wrongly say 62.5%");
        (v.Numerator, v.Denominator).ShouldBe((65m, 80m));
        v.Value.ShouldBe(81.3m);
    }

    [Fact]
    public async Task Cost_per_shipment_cost_per_ton_and_cost_per_ton_km_use_the_contract_freight_and_leave_out_what_has_no_price_or_distance()
    {
        var p = new TestProvider();
        p.Shipments.Add(F.Ship("S1", weight: 10_000, km: 500, freight: 50_000));
        p.Shipments.Add(F.Ship("S2", weight: 5_000, km: 200, freight: 20_000));
        p.Shipments.Add(F.Ship("S3", weight: 4_000, km: null, freight: 10_000)); // no distance: out of ton-km only
        p.Shipments.Add(F.Ship("S4", weight: 2_000, km: 100, freight: null)); // no freight yet: out of every cost figure
        p.Shipments.Add(F.Ship("S5", status: "Cancelled", freight: 99_999));

        (await Calc("COST_PER_SHIPMENT", p)).Value.ShouldBe(26_666.67m);
        (await Calc("FREIGHT_SPEND", p)).Value.ShouldBe(80_000m);
        var perTon = await Calc("COST_PER_TON", p);
        (perTon.Numerator, perTon.Denominator).ShouldBe((80_000m, 19m));
        perTon.Value.ShouldBe(4_210.53m);
        var tonKm = await Calc("COST_PER_TON_KM", p);
        tonKm.Denominator.ShouldBe((10m * 500m) + (5m * 200m));
        tonKm.Value.ShouldBe(11.67m);
        tonKm.Excluded.ShouldBe(1);
    }

    [Fact]
    public async Task Claims_rate_counts_deliveries_with_a_claim_once_each_over_completed_deliveries()
    {
        var p = new TestProvider();
        for (var i = 1; i <= 10; i++)
        {
            p.Deliveries.Add(F.Delivery($"D{i}"));
        }

        p.Deliveries.Add(F.Delivery("D11", status: "Failed", onTime: null));
        p.Discrepancies.Add(new DiscrepancyFact("D1", "SH-1", "Acme", F.A, "Alpha", "SKU", 10, 10, 8, 2, "Shortage", null, null, true, "CLM-1", null, F.Day));
        p.Discrepancies.Add(new DiscrepancyFact("D1", "SH-1", "Acme", F.A, "Alpha", "SKU2", 10, 10, 9, 1, "Damage", null, "Crushed", true, "CLM-2", null, F.Day)); // same delivery: one claim
        p.Discrepancies.Add(new DiscrepancyFact("D2", "SH-1", "Acme", F.A, "Alpha", "SKU", 10, 10, 9, 1, "Shortage", null, null, true, null, null, F.Day)); // no claim

        var r = await Calc("CLAIMS_RATE", p);

        (r.Numerator, r.Denominator).ShouldBe((1m, 10m));
        r.Value.ShouldBe(10m);
        (await Calc("SHORTAGE_PCT", p)).Numerator.ShouldBe(2m);
        (await Calc("DAMAGE_PCT", p)).Numerator.ShouldBe(1m);
    }

    [Fact]
    public async Task Tracking_coverage_is_trips_with_tracking_started_over_trips_that_departed()
    {
        var p = new TestProvider();
        p.Trips.Add(F.Trip("Healthy"));
        p.Trips.Add(F.Trip("Stale"));
        p.Trips.Add(F.Trip("Lost"));
        p.Trips.Add(F.Trip("NotStarted"));
        p.Trips.Add(F.Trip("Completed", "Completed"));
        p.Trips.Add(F.Trip("NotStarted", "NotStarted")); // has not left: not in the denominator

        var r = await Calc("TRACKING_COVERAGE", p);

        (r.Numerator, r.Denominator).ShouldBe((4m, 5m));
        r.Value.ShouldBe(80m);
        (await Calc("TRACKING_LOST", p)).Value.ShouldBe(1m);
    }

    [Fact]
    public async Task ETA_accuracy_is_trips_whose_last_eta_was_within_tolerance_of_the_actual_arrival()
    {
        var p = new TestProvider();
        var planned = F.Noon;
        p.Trips.Add(F.Trip("Completed", "Completed", plannedEta: planned, latestEta: planned, actual: planned.AddMinutes(10)));
        p.Trips.Add(F.Trip("Completed", "Completed", plannedEta: planned, latestEta: planned, actual: planned.AddMinutes(-25)));
        p.Trips.Add(F.Trip("Completed", "Completed", plannedEta: planned, latestEta: planned, actual: planned.AddMinutes(95)));
        p.Trips.Add(F.Trip("Completed", "Completed", plannedEta: planned, latestEta: null, actual: planned)); // no ETA was ever made: out
        p.Trips.Add(F.Trip("Healthy", "InTransit", plannedEta: planned, latestEta: planned.AddHours(1), actual: null)); // still on the road: out

        var r = await Calc("ETA_ACCURACY", p);
        var error = await Calc("ETA_ERROR_MIN", p);

        (r.Numerator, r.Denominator).ShouldBe((2m, 3m));
        r.Value.ShouldBe(66.7m);
        error.Value.ShouldBe(43m, "(10 + 25 + 95) / 3 minutes");
    }

    [Fact]
    public async Task Consolidation_savings_are_the_separate_cost_less_the_consolidated_cost_of_consolidated_trips_only()
    {
        var p = new TestProvider();
        p.Vehicles.Add(F.Plan(consolidated: true, cost: 40_000, separate: 55_000));
        p.Vehicles.Add(F.Plan(consolidated: true, cost: 20_000, separate: 26_000));
        p.Vehicles.Add(F.Plan(consolidated: false, cost: 30_000, separate: 99_999));

        var r = await Calc("CONSOLIDATION_SAVINGS", p);

        r.Numerator.ShouldBe(21_000m);
        r.Denominator.ShouldBe(2m);
    }

    [Fact]
    public async Task Mix_percentages_count_each_service_over_all_shipments_cancelled_excluded()
    {
        var p = new TestProvider();
        p.Shipments.AddRange([F.Ship("1", "FTL"), F.Ship("2", "FTL"), F.Ship("3", "PTL"), F.Ship("4", "Dedicated"), F.Ship("5", "FTL", status: "Cancelled")]);

        (await Calc("FTL_PCT", p)).Value.ShouldBe(50m);
        (await Calc("PTL_PCT", p)).Value.ShouldBe(25m);
        (await Calc("DEDICATED_PCT", p)).Value.ShouldBe(25m);
        (await Calc("SHIPMENTS", p)).Value.ShouldBe(4m);
    }

    [Fact]
    public async Task Open_critical_exceptions_come_from_every_module_and_ignore_resolved_ones()
    {
        var p = new TestProvider();
        ExceptionFact E(string module, string severity, string status) => new(module, Guid.NewGuid().ToString(), "X", severity, "SH-1", F.A, "Alpha", null, F.Noon, null, null, status);
        p.Exceptions.AddRange([E("Tracking", "Critical", "Open"), E("Delivery", "Critical", "InProgress"), E("Transporter", "Critical", "Resolved"), E("Tracking", "High", "Open")]);

        (await Calc("OPEN_CRITICAL_EXCEPTIONS", p)).Value.ShouldBe(2m);
    }

    [Fact]
    public async Task Loads_without_a_rate_are_summed_from_coverage()
    {
        var p = new TestProvider();
        p.CoverageRows.Add(new CoverageFact("A → B", "A", "B", "FTL", 10, "Lane", 2, 0, 0));
        p.CoverageRows.Add(new CoverageFact("C → D", "C", "D", "FTL", 7, "None", 0, 0, 7));

        (await Calc("LOADS_WITHOUT_RATE", p)).Value.ShouldBe(7m);
    }

    [Fact]
    public async Task A_comparison_reports_the_change_and_direction_and_says_which_way_is_better()
    {
        var p = new TestProvider();
        var earlier = F.Day.AddDays(-30);
        p.Deliveries.Add(F.Delivery("now1", onTime: true) with { Date = F.Day });
        p.Deliveries.Add(F.Delivery("now2", onTime: false) with { Date = F.Day });
        p.Deliveries.Add(F.Delivery("old1", onTime: true) with { Date = earlier });
        p.Deliveries.Add(F.Delivery("old2", onTime: true) with { Date = earlier });
        var range = new DateRange(F.Day.AddDays(-1), F.Day.AddDays(1));
        var facts = new ReportFacts(F.All(p), range, new ReportFilters(), ReportTestKit.Admin(), new ReportSettings(), ReportTestKit.Now, new DateRange(earlier.AddDays(-5), F.Day.AddDays(5)));
        var ctx = new ReportContext(facts, PeriodKind.Custom, CompareMode.PreviousPeriod, "month");

        var card = await ctx.CardAsync("OTD");

        card.Value.ShouldBe(50m);
        card.Previous.ShouldBeNull("the previous 3 days hold no deliveries: not measurable, not 0%");
        card.Trend.ShouldBe("none");
        card.Assessment.ShouldBe("neutral");
    }
}
