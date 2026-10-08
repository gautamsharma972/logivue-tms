using Tms.Modules.Reports.Application;
using Tms.Modules.Reports.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Reports;

public class ScheduleTests
{
    private static readonly DateTimeOffset Utc = new(2026, 10, 7, 3, 0, 0, TimeSpan.Zero); // 08:30 in India

    [Fact]
    public void A_daily_schedule_runs_at_the_local_time_of_its_time_zone()
    {
        var next = ScheduleCalculator.Next(ScheduleType.Daily, new ScheduleDefinition("09:00"), "Asia/Kolkata", Utc);
        next.ShouldBe(new DateTimeOffset(2026, 10, 7, 3, 30, 0, TimeSpan.Zero), "09:00 IST is 03:30 UTC, still today");

        var later = ScheduleCalculator.Next(ScheduleType.Daily, new ScheduleDefinition("08:00"), "Asia/Kolkata", Utc);
        later.ShouldBe(new DateTimeOffset(2026, 10, 8, 2, 30, 0, TimeSpan.Zero), "08:00 IST has passed today, so tomorrow");

        ScheduleCalculator.Next(ScheduleType.Daily, new ScheduleDefinition("08:00"), "UTC", Utc).ShouldBe(new DateTimeOffset(2026, 10, 7, 8, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_weekly_schedule_waits_for_its_weekday()
    {
        var next = ScheduleCalculator.Next(ScheduleType.Weekly, new ScheduleDefinition("09:00", "Monday"), "Asia/Kolkata", Utc); // 7 Oct 2026 is a Wednesday
        next.ShouldBe(new DateTimeOffset(2026, 10, 12, 3, 30, 0, TimeSpan.Zero));
        TimeZoneInfo.ConvertTime(next, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")).DayOfWeek.ShouldBe(DayOfWeek.Monday);
    }

    [Fact]
    public void A_monthly_schedule_uses_the_day_of_the_month_and_the_last_day_when_it_is_zero_or_too_long_for_the_month()
    {
        ScheduleCalculator.Next(ScheduleType.Monthly, new ScheduleDefinition("07:00", DayOfMonth: 1), "Asia/Kolkata", Utc).ShouldBe(new DateTimeOffset(2026, 11, 1, 1, 30, 0, TimeSpan.Zero));
        ScheduleCalculator.Next(ScheduleType.Monthly, new ScheduleDefinition("07:00", DayOfMonth: 0), "Asia/Kolkata", Utc).ShouldBe(new DateTimeOffset(2026, 10, 31, 1, 30, 0, TimeSpan.Zero));
        ScheduleCalculator.Next(ScheduleType.Monthly, new ScheduleDefinition("07:00", DayOfMonth: 31), "Asia/Kolkata", new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero)).ShouldBe(new DateTimeOffset(2026, 11, 30, 1, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public void A_custom_schedule_repeats_every_so_many_minutes_and_is_never_more_often_than_the_minimum()
    {
        ScheduleCalculator.Next(ScheduleType.Custom, new ScheduleDefinition(EveryMinutes: 120), "UTC", Utc).ShouldBe(Utc.AddHours(2));
        ScheduleCalculator.Validate(ScheduleType.Custom, new ScheduleDefinition(EveryMinutes: 5), "UTC").ShouldNotBeNull();
        ScheduleCalculator.Validate(ScheduleType.Custom, new ScheduleDefinition(EveryMinutes: 60), "UTC").ShouldBeNull();
    }

    [Fact]
    public void A_bad_time_zone_weekday_or_time_is_refused()
    {
        ScheduleCalculator.Validate(ScheduleType.Daily, new ScheduleDefinition("08:00"), "Nowhere/Land").ShouldNotBeNull();
        ScheduleCalculator.Validate(ScheduleType.Daily, new ScheduleDefinition("25:99"), "UTC").ShouldNotBeNull();
        ScheduleCalculator.Validate(ScheduleType.Weekly, new ScheduleDefinition("08:00", "Funday"), "UTC").ShouldNotBeNull();
        ScheduleCalculator.Validate(ScheduleType.Monthly, new ScheduleDefinition("08:00", DayOfMonth: 40), "UTC").ShouldNotBeNull();
    }
}

public class ShapingTests
{
    private static ReportRow Row(string group, decimal num, decimal den, decimal? value = null) => new() { ["g"] = group, ["n"] = num, ["d"] = den, ["v"] = value, ["c"] = 1 };

    [Fact]
    public void Grouping_re_derives_a_ratio_from_summed_parts_not_from_the_rows_percentages()
    {
        var rows = new[] { Row("a", 1, 1, 100), Row("a", 1, 9, 11.1m), Row("b", 0, 4, 0) };
        var grouped = ReportShaper.Group(rows, ["g"], [new MeasureSpec("c", MeasureKind.Count), new MeasureSpec("n", MeasureKind.Sum), new MeasureSpec("d", MeasureKind.Sum), new MeasureSpec("pct", MeasureKind.Ratio, "n", "d", 100m)]);

        var a = grouped.Single(r => (string)r["g"]! == "a");
        a["c"].ShouldBe(2);
        a["pct"].ShouldBe(20m, "2 of 10, not the average of 100% and 11.1%");
        grouped.Single(r => (string)r["g"]! == "b")["pct"].ShouldBe(0m);
    }

    [Fact]
    public void A_group_with_nothing_to_divide_by_has_no_ratio_not_zero_and_missing_values_are_left_out_of_sums_and_averages()
    {
        var rows = new[] { new ReportRow { ["g"] = "x", ["n"] = 0m, ["d"] = 0m, ["v"] = null }, new ReportRow { ["g"] = "x", ["n"] = 0m, ["d"] = 0m, ["v"] = null } };
        var grouped = ReportShaper.Group(rows, ["g"], [new MeasureSpec("v", MeasureKind.Avg), new MeasureSpec("n", MeasureKind.Sum), new MeasureSpec("d", MeasureKind.Sum), new MeasureSpec("pct", MeasureKind.Ratio, "n", "d", 100m)]);

        grouped.Single()["pct"].ShouldBeNull();
        grouped.Single()["v"].ShouldBeNull();
    }

    [Fact]
    public void Sorting_puts_nothing_last_when_descending_and_orders_numbers_as_numbers()
    {
        var rows = new[] { new ReportRow { ["x"] = 9 }, new ReportRow { ["x"] = null }, new ReportRow { ["x"] = 100m }, new ReportRow { ["x"] = 20 } };
        ReportShaper.Sort(rows, [("x", true)]).Select(r => r["x"]).ShouldBe([100m, 20, 9, null]);
        ReportShaper.Sort(rows, [("x", false)]).Select(r => r["x"]).ShouldBe([null, 9, 20, 100m]);
    }

    [Fact]
    public void Column_filters_match_text_case_insensitively()
    {
        var rows = new[] { new ReportRow { ["t"] = "Shree Roadlines" }, new ReportRow { ["t"] = "Bharat" } };
        ReportShaper.FilterColumns(rows, new Dictionary<string, string> { ["t"] = "shree" }).Count.ShouldBe(1);
    }
}

public class PeriodAndCalendarTests
{
    [Fact]
    public void Periods_resolve_and_compare_as_the_calendar_says()
    {
        var today = new DateOnly(2026, 10, 7);
        Periods.Resolve(PeriodKind.Monthly, today, null, null, 30).ShouldBe(new DateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        Periods.Resolve(PeriodKind.Quarterly, today, null, null, 30).ShouldBe(new DateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)));
        Periods.Resolve(PeriodKind.Ytd, today, null, null, 30).ShouldBe(new DateRange(new DateOnly(2026, 1, 1), today));
        Periods.Resolve(PeriodKind.Rolling30, today, null, null, 30).Days.ShouldBe(30);
        Periods.Resolve(PeriodKind.Weekly, today, null, null, 30).From.DayOfWeek.ShouldBe(DayOfWeek.Monday);

        var october = Periods.Resolve(PeriodKind.Monthly, today, null, null, 30);
        Periods.Compare(october, CompareMode.PreviousPeriod, PeriodKind.Monthly).ShouldBe(new DateRange(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30)));
        Periods.Compare(october, CompareMode.SamePeriodLastYear, PeriodKind.Monthly).ShouldBe(new DateRange(new DateOnly(2025, 10, 1), new DateOnly(2025, 10, 31)));
        var days = new DateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 10));
        Periods.Compare(days, CompareMode.PreviousPeriod, PeriodKind.Custom).ShouldBe(new DateRange(new DateOnly(2026, 9, 21), new DateOnly(2026, 9, 30)));
    }

    [Fact]
    public void Ageing_counts_working_days_when_the_organisation_says_so_and_skips_holidays()
    {
        // Fri 2 Oct → Wed 7 Oct 2026: 5 calendar days; Sat is a working day by default, Sunday is not; 5 Oct is a holiday here.
        var settings = new ReportSettings { AgeingUsesWorkingDays = true, Holidays = ["2026-10-05"] };
        var from = new DateTimeOffset(2026, 10, 2, 5, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 10, 7, 5, 0, 0, TimeSpan.Zero);
        Kit2.Age(from, to, new ReportSettings()).ShouldBe(5);
        Kit2.Age(from, to, settings).ShouldBe(3, "Sat 3, Tue 6, Wed 7");
    }

    [Fact]
    public void Age_buckets_come_from_the_configured_bounds()
    {
        int[] bounds = [1, 3, 7, 15, 30];
        Kit2.Bucket(0, bounds).ShouldBe("0–1 days");
        Kit2.Bucket(3, bounds).ShouldBe("2–3 days");
        Kit2.Bucket(7, bounds).ShouldBe("4–7 days");
        Kit2.Bucket(31, bounds).ShouldBe(">30 days");
        Kit2.Bucket(5, [2, 5]).ShouldBe("3–5 days");
    }
}

/// <summary>The catalogue's internal helpers, reached through the report that uses them.</summary>
internal static class Kit2
{
    public static int Age(DateTimeOffset from, DateTimeOffset to, ReportSettings s) => s.Calendar.DaysBetween(DateOnly.FromDateTime(from.UtcDateTime.AddMinutes(330)), DateOnly.FromDateTime(to.UtcDateTime.AddMinutes(330)), s.AgeingUsesWorkingDays);

    public static string Bucket(int days, int[] bounds)
    {
        var low = 0;
        foreach (var upper in bounds)
        {
            if (days <= upper)
            {
                return low == upper ? $"{upper} day" : $"{low}–{upper} days";
            }

            low = upper + 1;
        }

        return $">{bounds[^1]} days";
    }
}

public class ReportBehaviourTests
{
    private static async Task<ReportData> Run(string code, TestProvider p, ReportFilters? filters = null, ReportPrincipal? principal = null, ReportSettings? settings = null)
    {
        var facts = new ReportFacts(F.All(p), new DateRange(F.Day.AddDays(-5), F.Day.AddDays(5)), filters ?? new ReportFilters(), principal ?? ReportTestKit.Admin(), settings ?? new ReportSettings(), ReportTestKit.Now);
        return await ReportCatalogue.Find(code)!.Build(new ReportContext(facts, PeriodKind.Custom, CompareMode.PreviousPeriod, "month"), CancellationToken.None);
    }

    [Fact]
    public async Task A_transporter_user_sees_only_their_own_company_in_every_kind_of_fact_even_if_the_provider_returns_everyone()
    {
        var p = new TestProvider();
        p.Deliveries.AddRange([F.Delivery("mine", carrier: F.A, name: "Alpha"), F.Delivery("theirs", carrier: F.B, name: "Beta")]);
        p.Shipments.AddRange([F.Ship("S-A", carrier: F.A), F.Ship("S-B", carrier: F.B, carrierName: "Beta")]);
        p.Pods.AddRange([F.Pod("P-A", carrier: F.A), F.Pod("P-B", carrier: F.B, name: "Beta")]);
        p.CoverageRows.Add(new CoverageFact("A → B", "A", "B", "FTL", 1, "Lane", 1, 0, 0)); // carries no company: never shown to a company user
        var vendor = ReportTestKit.Vendor(F.A);

        var data = await Run("R17_DELIVERY_PERFORMANCE", p, principal: vendor);
        data.Rows.Select(r => r["delivery"]).ShouldBe(["mine"]);

        var facts = new ReportFacts(F.All(p), new DateRange(F.Day.AddDays(-5), F.Day.AddDays(5)), new ReportFilters(), vendor, new ReportSettings(), ReportTestKit.Now);
        (await facts.Shipments()).Select(s => s.ShipmentRef).ShouldBe(["S-A"]);
        (await facts.Pods()).Select(s => s.PodRef).ShouldBe(["P-A"]);
        (await facts.Coverage()).ShouldBeEmpty();
    }

    [Fact]
    public async Task A_user_limited_to_customers_sees_only_those_customers_and_nothing_that_cannot_be_judged_by_customer()
    {
        var p = new TestProvider();
        p.Deliveries.AddRange([F.Delivery("acme", customer: "Acme"), F.Delivery("other", customer: "Other Co")]);
        p.Trips.Add(F.Trip()); // a trip carries no customer: fail closed
        var scoped = ReportTestKit.Admin() with { Scopes = new Dictionary<string, IReadOnlyList<string>> { ["customer"] = ["Acme"] } };

        (await Run("R17_DELIVERY_PERFORMANCE", p, principal: scoped)).Rows.Select(r => r["delivery"]).ShouldBe(["acme"]);
        var facts = new ReportFacts(F.All(p), new DateRange(F.Day.AddDays(-5), F.Day.AddDays(5)), new ReportFilters(), scoped, new ReportSettings(), ReportTestKit.Now);
        (await facts.Trips()).ShouldBeEmpty();
    }

    [Fact]
    public async Task POD_ageing_runs_three_separate_clocks_and_buckets_by_the_configured_days()
    {
        var p = new TestProvider();
        var now = ReportTestKit.Now;
        p.Pods.Add(F.Pod("NOSUB", withinSla: false, status: "Pending", submitted: null, completed: now.AddDays(-10)) with { Date = F.Day });
        p.Pods.Add(F.Pod("REVIEW", status: "Submitted", submitted: now.AddDays(-2), completed: now.AddDays(-3)) with { Date = F.Day });
        p.Pods.Add(F.Pod("REJ", status: "Rejected", submitted: now.AddDays(-9), completed: now.AddDays(-12)) with { RejectedAt = now.AddDays(-5), RejectionReason = "Signature missing", Date = F.Day });

        var sub = await Run("R19_POD_AGEING", p, new ReportFilters([new("clock", "Submission")]));
        sub.Rows.Select(r => (string)r["pod"]!).ShouldContain("NOSUB");
        sub.Rows.Single(r => (string)r["pod"]! == "NOSUB")["bucket"].ShouldBe("8–15 days");

        var resub = await Run("R19_POD_AGEING", p, new ReportFilters([new("clock", "Resubmission")]));
        resub.Rows.Single()["pod"].ShouldBe("REJ");
        resub.Rows.Single()["ageDays"].ShouldBe(5);
        var review = await Run("R19_POD_AGEING", p, new ReportFilters([new("clock", "Review")]));
        review.Rows.Select(r => (string)r["pod"]!).ShouldContain("REVIEW");
    }

    [Fact]
    public async Task OTP_OTD_report_separates_carrier_attributable_delay_from_the_rest_and_never_charges_a_delay_nobody_judged()
    {
        var p = new TestProvider();
        p.Shipments.Add(F.Ship("SH-1", plannedPickup: F.Noon, actualPickup: F.Noon.AddMinutes(10)));
        p.Deliveries.Add(F.Delivery("D1", onTime: false, responsibility: "Carrier"));
        p.Deliveries.Add(F.Delivery("D2", onTime: false, responsibility: "NonCarrier"));
        p.Deliveries.Add(F.Delivery("D3", onTime: false, responsibility: null));

        var row = (await Run("R14_OTP_OTD", p)).Rows.Single();

        ((decimal)row["carrierDelayMin"]!).ShouldBe(300m);
        ((decimal)row["nonCarrierDelayMin"]!).ShouldBe(300m);
        ((decimal)row["unattributedDelayMin"]!).ShouldBe(300m);
    }

    [Fact]
    public async Task Cost_against_performance_classes_carriers_by_the_median_and_does_not_class_one_that_cannot_be_measured()
    {
        var p = new TestProvider();
        var carriers = new (Guid Id, string Name, decimal Freight, bool OnTime)[]
        {
            (Guid.NewGuid(), "Cheap Good", 20_000, true), (Guid.NewGuid(), "Cheap Poor", 22_000, false), (Guid.NewGuid(), "Dear Good", 60_000, true), (Guid.NewGuid(), "Dear Poor", 64_000, false),
        };
        foreach (var c in carriers)
        {
            p.Shipments.Add(F.Ship($"S-{c.Name}", carrier: c.Id, carrierName: c.Name, freight: c.Freight));
            p.Deliveries.Add(F.Delivery($"D-{c.Name}", onTime: c.OnTime, carrier: c.Id, name: c.Name));
        }

        p.Shipments.Add(F.Ship("S-new", carrier: Guid.NewGuid(), carrierName: "No Prices Yet", freight: null));

        var data = await Run("R38_COST_VS_PERFORMANCE", p);
        string Class(string name) => (string)data.Rows.Single(r => (string)r["transporter"]! == name)["quadrant"]!;

        Class("Cheap Good").ShouldBe("Low cost / High performance");
        Class("Cheap Poor").ShouldBe("Low cost / Low performance");
        Class("Dear Good").ShouldBe("High cost / High performance");
        Class("Dear Poor").ShouldBe("High cost / Low performance");
        Class("No Prices Yet").ShouldBe("Not classified");
    }

    [Fact]
    public async Task Cost_vs_service_says_plainly_whether_cheaper_carriers_deliver_worse_and_declines_to_conclude_from_too_few_groups()
    {
        var p = new TestProvider();
        var carriers = new (string Name, decimal Freight, bool OnTime)[] { ("A", 10_000, false), ("B", 12_000, false), ("C", 50_000, true), ("D", 55_000, true) };
        foreach (var c in carriers)
        {
            var id = Guid.NewGuid();
            p.Shipments.Add(F.Ship($"S-{c.Name}", carrier: id, carrierName: c.Name, freight: c.Freight));
            p.Deliveries.Add(F.Delivery($"D-{c.Name}", onTime: c.OnTime, carrier: id, name: c.Name));
        }

        var reading = (string)(await Run("R39_COST_VS_SERVICE", p)).Sections.Single().Items[0].Value!;
        reading.ShouldStartWith("Yes: the cheaper half delivers on time");

        var tooFew = new TestProvider();
        tooFew.Shipments.Add(F.Ship("S1", freight: 10_000));
        tooFew.Deliveries.Add(F.Delivery("D1"));
        ((string)(await Run("R39_COST_VS_SERVICE", tooFew)).Sections.Single().Items[0].Value!).ShouldStartWith("Too few groups");
    }

    [Fact]
    public async Task Rate_coverage_adds_lanes_that_carried_loads_but_have_no_rate_at_all()
    {
        var p = new TestProvider();
        p.Shipments.Add(F.Ship("S1", origin: "Mumbai", destination: "Pune"));
        p.Shipments.Add(F.Ship("S2", origin: "Nagpur", destination: "Hyderabad"));
        p.Rates.Add(new RateFact("CN-1", 1, F.A, "Alpha", "R1", 1, "Mumbai", "Pune", null, "32 FT", "FTL", null, null, null, 30_000, "PerTrip", null, null, null, null));

        var rows = (await Run("R32_RATE_COVERAGE", p)).Rows;

        rows.Single(r => (string)r["lane"]! == "Mumbai → Pune")["coverType"].ShouldBe("Lane");
        var none = rows.Single(r => (string)r["lane"]! == "Nagpur → Hyderabad");
        none["coverType"].ShouldBe("None");
        none["loadsWithoutRate"].ShouldBe(1);
    }

    [Fact]
    public async Task Shipment_360_joins_every_module_by_shipment_and_hides_freight_from_people_without_commercial_access()
    {
        var p = new TestProvider();
        p.Shipments.Add(F.Ship("SH-10025"));
        p.Vehicles.Add(F.Plan());
        p.Deliveries.Add(F.Delivery("D1", onTime: false, responsibility: "Carrier") with { ShipmentRef = "SH-10025" });
        p.Pods.Add(F.Pod("P1", status: "Rejected") with { ShipmentRef = "SH-10025", RejectedAt = F.Noon, RejectionReason = "Stamp missing" });
        p.Trips.Add(F.Trip("Completed", "Completed") with { ShipmentRef = "SH-10025" });
        p.Ratings.Add(new RatingFact("SH-10025", F.A, "Alpha", "CN-1", 2, "R1", 1, "Mumbai → Pune", 10_000, 500, 30, "10-15t", 38_000, 1_520, 1_800, 0, 41_320, "1.0", F.Noon));
        var filters = new ReportFilters([new(FilterNames.Shipment, "SH-10025")]);

        var finance = await Run("R36_SHIPMENT_360", p, filters, ReportTestKit.Admin());
        finance.Sections.Select(s => s.Key).ShouldBe(["order", "planning", "vehicle", "transporter", "contract", "tracking", "delivery", "pod", "discrepancies", "exceptions", "freight", "timeline"]);
        finance.Sections.Single(s => s.Key == "freight").Items.Single(i => i.Label == "Final freight").Value.ShouldBe(41_320m);
        finance.Sections.Single(s => s.Key == "timeline").Table!.Select(r => (string)r["event"]!).ShouldContain(e => e.Contains("Stamp missing", StringComparison.Ordinal));

        var service = ReportTestKit.Admin() with { Permissions = new HashSet<string> { ReportingPermissions.Read, ReportingPermissions.Cross } };
        var plain = await Run("R36_SHIPMENT_360", p, filters, service);
        plain.Sections.Select(s => s.Key).ShouldNotContain("freight");
        plain.Sections.Select(s => s.Key).ShouldNotContain("contract");
    }

    [Fact]
    public async Task Every_cross_module_report_runs_over_the_demonstration_data_with_real_numbers()
    {
        var lanes = await ReportCatalogue.Find("R37_LANE_PERFORMANCE")!.Build(ReportTestKit.Context(), CancellationToken.None);
        lanes.Rows.Count.ShouldBeGreaterThan(5);
        lanes.Rows.Select(r => r["otd"]).Distinct().Count().ShouldBeGreaterThan(1, "demonstration numbers must not be flat");

        var exec = await ReportCatalogue.Find("R01_EXECUTIVE_DASHBOARD")!.Build(ReportTestKit.Context(), CancellationToken.None);
        exec.Cards.Count.ShouldBe(23);
        exec.Cards.Single(c => c.Code == "OTD").Value.ShouldNotBeNull();
        exec.Charts.Select(c => c.Id).ShouldBe(["freight-trend", "service-trend", "transporters", "lanes", "cost-vs-performance", "exceptions"]);
    }
}
