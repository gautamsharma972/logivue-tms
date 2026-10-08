using Tms.SharedKernel.Contracts;
using static Tms.Modules.Reports.Domain.Kit;

namespace Tms.Modules.Reports.Domain;

/// <summary>R09–R16: transporter management. Scores are the Transporters module's own; ranking and benchmarks compare carriers with the one KPI engine.</summary>
internal static class TransporterReports
{
    public static IEnumerable<ReportSpec> All()
    {
        yield return Scorecard();
        yield return Ranking();
        yield return Benchmark();
        yield return Tenders();
        yield return Placements();
        yield return OtpOtd();
        yield return PodCompliance();
        yield return Exceptions();
    }

    private static readonly (string Field, string Label, bool HigherIsBetter)[] ScoreMetrics =
    [
        ("overallScore", "Overall score", true), ("otp", "On-time pickup %", true), ("otd", "On-time delivery %", true), ("placement", "Vehicle placement %", true),
        ("tenderAcceptance", "Tender acceptance %", true), ("podCompliance", "POD compliance %", true), ("claimsRate", "Claims rate %", false), ("costPerformance", "Cost performance", true), ("availability", "Availability %", true),
    ];

    private static decimal? Metric(ScorecardFact s, string field) => field switch
    {
        "overallScore" => s.OverallScore, "otp" => s.Otp, "otd" => s.Otd, "placement" => s.Placement, "tenderAcceptance" => s.TenderAcceptance,
        "podCompliance" => s.PodCompliance, "claimsRate" => s.ClaimsRate, "costPerformance" => s.CostPerformance, _ => s.Availability,
    };

    private static ReportSpec Scorecard() => new()
    {
        Code = "R09_TRANSPORTER_SCORECARD", Name = "Transporter performance scorecard", Category = CatTransporters, Type = ReportType.Analytical, DataSource = "Transporters", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Transporters, VendorSafe = true, SortOrder = 90, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Each transporter's overall score and its parts for the period, against the previous period and the company benchmark. Scores are calculated by Transporter Management; this report shows them.",
        Columns = Cols(
            C("transporter", "Transporter", filter: true), C("overallScore", "Overall score", FieldType.Number), C("previousScore", "Previous period", FieldType.Number), C("change", "Change", FieldType.Number), C("trend", "Trend", FieldType.Status),
            C("benchmark", "Company benchmark", FieldType.Number), C("vsBenchmark", "Vs benchmark", FieldType.Number),
            C("otp", "OTP %", FieldType.Percent), C("otd", "OTD %", FieldType.Percent), C("placement", "Placement %", FieldType.Percent), C("tenderAcceptance", "Tender acceptance %", FieldType.Percent),
            C("podCompliance", "POD compliance %", FieldType.Percent), C("claimsRate", "Claims rate %", FieldType.Percent), C("costPerformance", "Cost performance", FieldType.Number), C("availability", "Availability %", FieldType.Percent),
            C("calculationVersion", "Calculation version")),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Lane),
        Drills = [new("transporter", "R17_DELIVERY_PERFORMANCE", "Their deliveries", Map((FilterNames.Transporter, "transporter"))), new("otp", "R14_OTP_OTD", "OTP / OTD detail", Map((FilterNames.Transporter, "transporter"))),
            new("placement", "R13_PLACEMENT_COMPLIANCE", "Placements", Map((FilterNames.Transporter, "transporter"))), new("podCompliance", "R15_POD_COMPLIANCE_BY_TRANSPORTER", "POD compliance", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("overallScore", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var now = await ctx.Facts.Scorecards();
            var before = await ctx.Facts.ForPeriod(ctx.Previous).Scorecards();
            var company = ctx.Principal.IsExternal ? [] : await ctx.Facts.Without(FilterNames.Transporter).Scorecards();
            foreach (var g in now.GroupBy(s => s.TransporterName).OrderBy(g => g.Key))
            {
                var prev = before.Where(s => s.TransporterName == g.Key).ToList();
                var overall = Avg(g.Select(s => s.OverallScore));
                var previous = Avg(prev.Select(s => s.OverallScore));
                var bench = company.Count == 0 ? null : Avg(company.Select(s => s.OverallScore));
                decimal? change = overall is not null && previous is not null ? Math.Round(overall.Value - previous.Value, 2) : null;
                var row = new ReportRow
                {
                    ["transporter"] = g.Key, ["overallScore"] = overall, ["previousScore"] = previous, ["change"] = change, ["trend"] = change switch { null => "Not comparable", > 0 => "Up", < 0 => "Down", _ => "Flat" },
                    ["benchmark"] = bench, ["vsBenchmark"] = overall is not null && bench is not null ? Math.Round(overall.Value - bench.Value, 2) : null, ["calculationVersion"] = g.Select(s => s.CalculationVersion).Distinct().OrderBy(v => v).LastOrDefault(),
                };
                foreach (var m in ScoreMetrics.Skip(1))
                {
                    row[m.Field] = Avg(g.Select(s => Metric(s, m.Field)));
                }

                d.Rows.Add(row);
            }

            d.Cards.Add(await ctx.CardAsync("TRANSPORTER_SCORE"));
            if (d.Rows.Count == 1)
            {
                var only = now.Where(s => s.TransporterName == (string)d.Rows[0]["transporter"]!).ToList();
                var prev = before.Where(s => s.TransporterName == (string)d.Rows[0]["transporter"]!).ToList();
                var table = ScoreMetrics.Select(m =>
                {
                    var cur = Avg(only.Select(s => Metric(s, m.Field)));
                    var pr = Avg(prev.Select(s => Metric(s, m.Field)));
                    var bench = company.Count == 0 ? null : Avg(company.Select(s => Metric(s, m.Field)));
                    return new ReportRow { ["metric"] = m.Label, ["current"] = cur, ["previous"] = pr, ["change"] = cur is not null && pr is not null ? cur - pr : null, ["benchmark"] = bench };
                }).ToList();
                d.Sections.Add(new ReportSection("metrics", "Against the previous period and the company", "Transporters", [], table,
                    Cols(C("metric", "Metric"), C("current", "Current", FieldType.Number), C("previous", "Previous", FieldType.Number), C("change", "Change", FieldType.Number), C("benchmark", "Company", FieldType.Number))));
            }

            d.Charts.Add(Chart("score-bar", "Overall score by transporter", "bar", "transporter",
                d.Rows.Where(r => r["overallScore"] is not null).Take(ctx.Settings.TopN * 2).Select(r => new ReportRow { ["transporter"] = r["transporter"], ["overallScore"] = r["overallScore"], ["benchmark"] = r["benchmark"] }),
                new ChartSeries("overallScore", "Score"), new ChartSeries("benchmark", "Company benchmark")));
            d.Notes.Add("A blank score means the Transporters module could not measure it for the period (for example no planned delivery time), not that it is zero.");
            d.Fixed = true;
            return d;
        },
    };

    private static readonly (string Key, string Label, string Code, bool Lower)[] RankKinds =
    [
        ("OverallScore", "Overall score", "TRANSPORTER_SCORE", false), ("OTP", "On-time pickup", "OTP", false), ("OTD", "On-time delivery", "OTD", false), ("Placement", "Placement compliance", "PLACEMENT_COMPLIANCE", false),
        ("POD", "POD compliance", "POD_COMPLIANCE", false), ("Claims", "Claims rate", "CLAIMS_RATE", true), ("Cost", "Cost per shipment", "COST_PER_SHIPMENT", true), ("TenderAcceptance", "Tender acceptance", "TENDER_ACCEPTANCE", false),
    ];

    private static ReportSpec Ranking() => new()
    {
        Code = "R10_TRANSPORTER_RANKING", Name = "Transporter ranking", Category = CatTransporters, Type = ReportType.Analytical, DataSource = "Transporters + Planning + Deliveries", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Transporters, SortOrder = 100, ExportFormats = "csv,xlsx,pdf",
        Description = "Rank transporters by overall score, OTP, OTD, placement, POD, claims, cost or tender acceptance, for a lane, region, vehicle type, service and period.",
        Columns = Cols(
            C("rank", "Rank", FieldType.Whole), C("transporter", "Transporter", filter: true), C("rankedBy", "Ranked by"), C("rankValue", "Value", FieldType.Number), C("shipments", "Shipments", FieldType.Whole),
            C("overallScore", "Overall score", FieldType.Number), C("otp", "OTP %", FieldType.Percent), C("otd", "OTD %", FieldType.Percent), C("placement", "Placement %", FieldType.Percent),
            C("podCompliance", "POD %", FieldType.Percent), C("claimsRate", "Claims %", FieldType.Percent), C("costPerShipment", "Cost / shipment", FieldType.Currency), C("tenderAcceptance", "Tender acceptance %", FieldType.Percent)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, "rankBy", FilterNames.Lane, FilterNames.Region, FilterNames.VehicleType, FilterNames.ServiceType),
        Drills = [new("transporter", "R09_TRANSPORTER_SCORECARD", "Scorecard", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("rank"),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var kind = RankKinds.FirstOrDefault(k => k.Key.Equals(ctx.Filters.Get("rankBy"), StringComparison.OrdinalIgnoreCase), RankKinds[0]);
            var shipments = await ctx.Facts.Shipments();
            var scores = await ctx.Facts.Scorecards();
            var names = shipments.Where(s => s.TransporterName is not null).Select(s => s.TransporterName!).Concat(scores.Select(s => s.TransporterName)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var kpis = await KpisBy(ctx.Facts.Without(FilterNames.Transporter), FilterNames.Transporter, names, RankKinds.Select(k => k.Code).Distinct());
            var rows = names.Select(n =>
            {
                var k = kpis[n];
                return new ReportRow
                {
                    ["transporter"] = n, ["shipments"] = shipments.Count(s => !s.IsCancelled && s.TransporterName == n), ["overallScore"] = k["TRANSPORTER_SCORE"].Value, ["otp"] = k["OTP"].Value, ["otd"] = k["OTD"].Value,
                    ["placement"] = k["PLACEMENT_COMPLIANCE"].Value, ["podCompliance"] = k["POD_COMPLIANCE"].Value, ["claimsRate"] = k["CLAIMS_RATE"].Value, ["costPerShipment"] = k["COST_PER_SHIPMENT"].Value, ["tenderAcceptance"] = k["TENDER_ACCEPTANCE"].Value,
                    ["rankedBy"] = kind.Label, ["rankValue"] = k[kind.Code].Value,
                };
            }).ToList();
            var ranked = (kind.Lower ? rows.Where(r => r["rankValue"] is not null).OrderBy(r => (decimal)r["rankValue"]!) : rows.Where(r => r["rankValue"] is not null).OrderByDescending(r => (decimal)r["rankValue"]!)).ToList();
            var position = 0;
            foreach (var r in ranked)
            {
                r["rank"] = ++position;
                d.Rows.Add(r);
            }

            foreach (var r in rows.Where(r => r["rankValue"] is null).OrderBy(r => (string)r["transporter"]!))
            {
                r["rank"] = null;
                d.Rows.Add(r);
            }

            d.Notes.Add($"Ranked by {kind.Label} ({(kind.Lower ? "lower is better" : "higher is better")}). Carriers with nothing to judge on this measure are listed last without a rank.");
            d.Charts.Add(Chart("ranking", $"{kind.Label} by transporter", "bar", "transporter", ranked.Take(ctx.Settings.TopN * 2).Select(r => new ReportRow { ["transporter"] = r["transporter"], ["value"] = r["rankValue"] }), new ChartSeries("value", kind.Label)));
            d.Fixed = true;
            return d;
        },
    };

    private static readonly (string Key, string Label, string Code)[] BenchMetrics =
    [
        ("OTD", "On-time delivery %", "OTD"), ("OTP", "On-time pickup %", "OTP"), ("Placement", "Placement compliance %", "PLACEMENT_COMPLIANCE"), ("POD", "POD compliance %", "POD_COMPLIANCE"),
        ("TenderAcceptance", "Tender acceptance %", "TENDER_ACCEPTANCE"), ("Claims", "Claims rate %", "CLAIMS_RATE"), ("Cost", "Cost per shipment", "COST_PER_SHIPMENT"),
    ];

    private static ReportSpec Benchmark() => new()
    {
        Code = "R11_TRANSPORTER_BENCHMARK", Name = "Transporter benchmark", Category = CatTransporters, Type = ReportType.Analytical, DataSource = "Transporters + Planning + Deliveries", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Transporters, SortOrder = 110, ExportFormats = "csv,xlsx,pdf",
        Description = "Each transporter against the average of its main lane, its main region, the best performer and the whole company, with the gap to each.",
        Columns = Cols(
            C("transporter", "Transporter", filter: true), C("metric", "Measure"), C("value", "Carrier value", FieldType.Number), C("primaryLane", "Main lane"), C("laneAverage", "Lane average", FieldType.Number),
            C("primaryRegion", "Main region"), C("regionAverage", "Region average", FieldType.Number), C("topPerformer", "Top performer"), C("topValue", "Top value", FieldType.Number), C("companyAverage", "Company average", FieldType.Number),
            C("gapToLane", "Gap to lane", FieldType.Number), C("gapToRegion", "Gap to region", FieldType.Number), C("gapToTop", "Gap to top", FieldType.Number), C("gapToCompany", "Gap to company", FieldType.Number)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, "metric", FilterNames.Transporter, FilterNames.Region, FilterNames.ServiceType),
        Drills = [new("transporter", "R09_TRANSPORTER_SCORECARD", "Scorecard", Map((FilterNames.Transporter, "transporter"))), new("primaryLane", "R37_LANE_PERFORMANCE", "Lane performance", Map((FilterNames.Lane, "primaryLane")))],
        Sorts = Sort("gapToCompany", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var m = BenchMetrics.FirstOrDefault(x => x.Key.Equals(ctx.Filters.Get("metric"), StringComparison.OrdinalIgnoreCase), BenchMetrics[0]);
            var kpi = KpiCatalogue.Find(m.Code)!;
            var everyone = ctx.Facts.Without(FilterNames.Transporter, FilterNames.Lane);
            var shipments = (await ctx.Facts.Shipments()).Where(s => s.TransporterName is not null && !s.IsCancelled).ToList();
            var names = shipments.Select(s => s.TransporterName!).Distinct().OrderBy(n => n).ToList();
            var all = new List<(string Name, KpiResult Result)>();
            foreach (var n in names)
            {
                all.Add((n, await KpiCatalogue.CalculateAsync(kpi, everyone.With(FilterNames.Transporter, n))));
            }
            var top = kpi.HigherIsBetter ? all.Where(a => a.Result.Value is not null).OrderByDescending(a => a.Result.Value).FirstOrDefault() : all.Where(a => a.Result.Value is not null).OrderBy(a => a.Result.Value).FirstOrDefault();
            var company = (await KpiCatalogue.CalculateAsync(kpi, everyone)).Value;
            foreach (var (name, result) in all)
            {
                var mine = shipments.Where(s => s.TransporterName == name).ToList();
                var lane = mine.GroupBy(s => s.Lane).OrderByDescending(g => g.Count()).First().Key;
                var region = mine.GroupBy(s => s.Region).OrderByDescending(g => g.Count()).First().Key;
                var laneAvg = (await KpiCatalogue.CalculateAsync(kpi, everyone.With(FilterNames.Lane, lane))).Value;
                var regionAvg = (await KpiCatalogue.CalculateAsync(kpi, everyone.With(FilterNames.Region, region))).Value;
                var v = result.Value;
                decimal? Gap(decimal? other) => v is not null && other is not null ? Math.Round(v.Value - other.Value, 2) : null;
                d.Rows.Add(new ReportRow
                {
                    ["transporter"] = name, ["metric"] = m.Label, ["value"] = v, ["primaryLane"] = lane, ["laneAverage"] = laneAvg, ["primaryRegion"] = region, ["regionAverage"] = regionAvg,
                    ["topPerformer"] = top.Name, ["topValue"] = top.Result?.Value, ["companyAverage"] = company, ["gapToLane"] = Gap(laneAvg), ["gapToRegion"] = Gap(regionAvg), ["gapToTop"] = Gap(top.Result?.Value), ["gapToCompany"] = Gap(company),
                });
            }

            d.Notes.Add($"{m.Label}: gaps are the transporter's value minus the benchmark ({(kpi.HigherIsBetter ? "positive is better" : "negative is better")}). Averages are of all shipments, not an average of averages.");
            d.Charts.Add(Chart("bench", $"{m.Label}: carrier against company", "bar", "transporter", d.Rows.Select(r => new ReportRow { ["transporter"] = r["transporter"], ["value"] = r["value"], ["company"] = r["companyAverage"] }),
                new ChartSeries("value", "Transporter"), new ChartSeries("company", "Company average")));
            d.Fixed = true;
            return d;
        },
    };

    private static ReportSpec Tenders() => new()
    {
        Code = "R12_TENDER_PERFORMANCE", Name = "Tender performance", Category = CatTransporters, Type = ReportType.Analytical, DataSource = "Planning (tenders)", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Transporters, VendorSafe = true, SortOrder = 120, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Tenders offered, viewed, accepted, rejected and expired, response and acceptance rates and response time, by transporter, lane, vehicle type or service.",
        Columns = Cols(
            C("transporter", "Transporter", filter: true), C("lane", "Lane", filter: true), C("vehicleType", "Vehicle type", filter: true), C("service", "Service", filter: true), C("month", "Month"), C("shipment", "Shipment"),
            C("offered", "Tenders offered", FieldType.Whole), C("viewed", "Viewed", FieldType.Whole), C("accepted", "Accepted", FieldType.Whole), C("rejected", "Rejected", FieldType.Whole), C("expired", "Expired", FieldType.Whole),
            C("pending", "Open", FieldType.Whole), C("responded", "Responded", FieldType.Whole, visible: false), C("closed", "Closed", FieldType.Whole, visible: false), C("counted", "Counted", FieldType.Whole, visible: false),
            C("responseRate", "Response rate %", FieldType.Percent), C("acceptanceRate", "Acceptance rate %", FieldType.Percent), C("responseMinutes", "Avg response time (min)", FieldType.Number), C("outcome", "Outcome", FieldType.Status, filter: true)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Lane, FilterNames.VehicleType, FilterNames.ServiceType, FilterNames.Status).Select(f => f.Name == FilterNames.Status ? f.With("tenderOutcome") : f).ToList(),
        Groupings = Groups(("transporter", "Transporter"), ("lane", "Lane"), ("vehicleType", "Vehicle type"), ("service", "Service"), ("month", "Month")),
        DefaultGroupBy = ["transporter"],
        Measures =
        [
            new("offered", MeasureKind.Sum), new("viewed", MeasureKind.Sum), new("accepted", MeasureKind.Sum), new("rejected", MeasureKind.Sum), new("expired", MeasureKind.Sum), new("pending", MeasureKind.Sum), new("responded", MeasureKind.Sum),
            new("closed", MeasureKind.Sum), new("counted", MeasureKind.Sum), new("responseRate", MeasureKind.Ratio, "responded", "closed", 100m), new("acceptanceRate", MeasureKind.Ratio, "accepted", "counted", 100m), new("responseMinutes", MeasureKind.Avg),
        ],
        Drills = [new("transporter", "R09_TRANSPORTER_SCORECARD", "Scorecard", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("offered", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var tenders = await ctx.Facts.Tenders();
            foreach (var t in tenders)
            {
                var counted = t.Outcome is "Accepted" or "Rejected" or "Expired";
                var closed = t.Outcome is not ("Pending" or "Withdrawn");
                d.Rows.Add(new ReportRow
                {
                    ["transporter"] = t.TransporterName, ["lane"] = t.Lane, ["vehicleType"] = t.VehicleType, ["service"] = t.Service, ["month"] = Bucket(Day(t.OfferedAt), "month"), ["shipment"] = t.ShipmentRef,
                    ["offered"] = 1, ["viewed"] = One(t.ViewedAt is not null), ["accepted"] = One(t.Outcome == "Accepted"), ["rejected"] = One(t.Outcome == "Rejected"), ["expired"] = One(t.Outcome == "Expired"),
                    ["pending"] = One(t.Outcome == "Pending"), ["responded"] = One(t.Outcome is "Accepted" or "Rejected"), ["closed"] = One(closed), ["counted"] = One(counted),
                    ["responseMinutes"] = t.RespondedAt is { } r ? Minutes(r, t.OfferedAt) : null, ["outcome"] = t.Outcome,
                });
            }

            d.Cards.Add(await ctx.CardAsync("TENDER_ACCEPTANCE"));
            d.Cards.Add(await ctx.CardAsync("TENDER_RESPONSE_RATE"));
            d.Charts.Add(Chart("outcomes", "Tender outcomes", "donut", "outcome", tenders.GroupBy(t => t.Outcome).Select(g => new ReportRow { ["outcome"] = g.Key, ["tenders"] = g.Count() }), new ChartSeries("tenders", "Tenders")));
            return d;
        },
    };

    private static ReportSpec Placements() => new()
    {
        Code = "R13_PLACEMENT_COMPLIANCE", Name = "Vehicle placement compliance", Category = CatTransporters, Type = ReportType.Operational, DataSource = "Transporters (placements)", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Transporters, VendorSafe = true, SortOrder = 130, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Whether vehicles were placed on time: requested, confirmed, reported and placed times, delay, no-shows and replacements.",
        Columns = Cols(
            C("placement", "Placement", filter: true), C("shipment", "Shipment"), C("transporter", "Transporter", filter: true), C("lane", "Lane", filter: true), C("vehicleType", "Vehicle type", filter: true),
            C("requestedAt", "Requested at", FieldType.DateTime), C("confirmedAt", "Confirmed at", FieldType.DateTime), C("reportedAt", "Reported at", FieldType.DateTime), C("placedAt", "Placed at", FieldType.DateTime),
            C("requiredBy", "Required by", FieldType.DateTime), C("outcome", "Outcome", FieldType.Status, filter: true), C("delayMinutes", "Delay (min)", FieldType.Whole), C("month", "Month"),
            C("placements", "Placements", FieldType.Whole), C("onTime", "On time", FieldType.Whole), C("late", "Late", FieldType.Whole), C("noShow", "No-show", FieldType.Whole), C("replaced", "Replacement", FieldType.Whole),
            C("compliance", "Compliance %", FieldType.Percent), C("lateMinutes", "Avg delay (min)", FieldType.Number)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Lane, FilterNames.VehicleType, FilterNames.ServiceType, FilterNames.Status).Select(f => f.Name == FilterNames.Status ? f.With("placementOutcome") : f).ToList(),
        Groupings = Groups(("transporter", "Transporter"), ("lane", "Lane"), ("vehicleType", "Vehicle type"), ("month", "Month")),
        Measures =
        [
            new("placements", MeasureKind.Count), new("onTime", MeasureKind.Sum), new("late", MeasureKind.Sum), new("noShow", MeasureKind.Sum), new("replaced", MeasureKind.Sum), new("compliance", MeasureKind.Ratio, "onTime", "placements", 100m), new("lateMinutes", MeasureKind.Avg),
        ],
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment"))), new("transporter", "R09_TRANSPORTER_SCORECARD", "Scorecard", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("requiredBy", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rows = await ctx.Facts.Placements();
            foreach (var p in rows)
            {
                d.Rows.Add(new ReportRow
                {
                    ["placement"] = p.PlacementRef, ["shipment"] = p.ShipmentRef, ["transporter"] = p.TransporterName, ["lane"] = p.Lane, ["vehicleType"] = p.VehicleType, ["requestedAt"] = p.RequestedAt, ["confirmedAt"] = p.ConfirmedAt,
                    ["reportedAt"] = p.ReportedAt, ["placedAt"] = p.PlacedAt, ["requiredBy"] = p.RequiredBy, ["outcome"] = p.Outcome, ["delayMinutes"] = p.DelayMinutes, ["month"] = Bucket(Day(p.RequiredBy), "month"),
                    ["placements"] = 1, ["onTime"] = One(p.Outcome == "OnTime"), ["late"] = One(p.Outcome == "Late"), ["noShow"] = One(p.Outcome == "NoShow"), ["replaced"] = One(p.Outcome == "Replaced"),
                    ["lateMinutes"] = p.Outcome == "Late" ? p.DelayMinutes : null,
                });
            }

            d.Cards.Add(await ctx.CardAsync("PLACEMENT_COMPLIANCE"));
            d.Totals.Add(new("placements", "Placement requests", rows.Count));
            d.Totals.Add(new("onTime", "On time", rows.Count(p => p.Outcome == "OnTime")));
            d.Totals.Add(new("late", "Late", rows.Count(p => p.Outcome == "Late")));
            d.Totals.Add(new("noShow", "No-show", rows.Count(p => p.Outcome == "NoShow")));
            d.Totals.Add(new("replaced", "Replacement", rows.Count(p => p.Outcome == "Replaced")));
            d.Totals.Add(new("delay", "Average delay of late placements (min)", Avg(rows.Where(p => p.Outcome == "Late").Select(p => (decimal?)p.DelayMinutes)), "number"));
            d.Charts.Add(Chart("outcomes", "Placement outcomes", "donut", "outcome", rows.GroupBy(p => p.Outcome).Select(g => new ReportRow { ["outcome"] = g.Key, ["placements"] = g.Count() }), new ChartSeries("placements", "Placements")));
            return d;
        },
    };

    private static ReportSpec OtpOtd() => new()
    {
        Code = "R14_OTP_OTD", Name = "OTP / OTD performance", Category = CatTransporters, Type = ReportType.Analytical, DataSource = "Planning + Deliveries", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Transporters, VendorSafe = true, SortOrder = 140, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Planned against actual pickup and delivery, the delay, and OTP / OTD, with delay split into carrier-attributable, other causes and not yet judged.",
        Columns = Cols(
            C("shipment", "Shipment", filter: true), C("transporter", "Transporter", filter: true), C("lane", "Lane", filter: true), C("month", "Month"),
            C("plannedPickup", "Planned pickup", FieldType.DateTime), C("actualPickup", "Actual pickup", FieldType.DateTime), C("pickupDelayMin", "Pickup delay (min)", FieldType.Whole), C("otp", "OTP", FieldType.Status),
            C("plannedDelivery", "Planned delivery", FieldType.DateTime), C("actualDelivery", "Actual delivery", FieldType.DateTime), C("deliveryDelayMin", "Delivery delay (min)", FieldType.Whole), C("otd", "OTD", FieldType.Status),
            C("carrierDelayMin", "Carrier-attributable delay (min)", FieldType.Whole), C("nonCarrierDelayMin", "Non-carrier delay (min)", FieldType.Whole), C("unattributedDelayMin", "Delay not yet judged (min)", FieldType.Whole),
            C("shipments", "Shipments", FieldType.Whole), C("otpMet", "OTP met", FieldType.Whole, visible: false), C("otpJudged", "OTP judged", FieldType.Whole, visible: false), C("otdMet", "OTD met", FieldType.Whole, visible: false),
            C("otdJudged", "OTD judged", FieldType.Whole, visible: false), C("otpPct", "OTP %", FieldType.Percent), C("otdPct", "OTD %", FieldType.Percent)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Lane, FilterNames.Customer, FilterNames.Region, FilterNames.ServiceType, FilterNames.Shipment),
        Groupings = Groups(("transporter", "Transporter"), ("lane", "Lane"), ("month", "Month")),
        DefaultGroupBy = ["transporter"],
        Measures =
        [
            new("shipments", MeasureKind.Count), new("otpMet", MeasureKind.Sum), new("otpJudged", MeasureKind.Sum), new("otdMet", MeasureKind.Sum), new("otdJudged", MeasureKind.Sum), new("otpPct", MeasureKind.Ratio, "otpMet", "otpJudged", 100m),
            new("otdPct", MeasureKind.Ratio, "otdMet", "otdJudged", 100m), new("pickupDelayMin", MeasureKind.Avg), new("deliveryDelayMin", MeasureKind.Avg), new("carrierDelayMin", MeasureKind.Sum), new("nonCarrierDelayMin", MeasureKind.Sum), new("unattributedDelayMin", MeasureKind.Sum),
        ],
        Drills = [new("deliveryDelayMin", "R17_DELIVERY_PERFORMANCE", "Late deliveries", Map((FilterNames.Transporter, "transporter"), ("onTime", "=No"))), new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("deliveryDelayMin", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var shipments = (await ctx.Facts.Shipments()).Where(s => !s.IsCancelled).ToList();
            var deliveries = (await ctx.Facts.Deliveries()).Where(x => x.ShipmentRef is not null).GroupBy(x => x.ShipmentRef!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            foreach (var s in shipments)
            {
                deliveries.TryGetValue(s.ShipmentRef, out var drops);
                drops ??= [];
                var pickupJudged = s.ActualPickupAt is not null && s.PlannedPickupAt is not null;
                var pickupDelay = pickupJudged ? Minutes(s.ActualPickupAt, s.PlannedPickupAt) : null;
                var pickupMet = pickupJudged && s.ActualPickupAt!.Value <= s.PlannedPickupAt!.Value.AddMinutes(ctx.Settings.OtpGraceMinutes);
                var judgedDrops = drops.Where(x => x.OnTime is not null && x.Status is "Delivered" or "PartiallyDelivered").ToList();
                var deliveryJudged = judgedDrops.Count > 0;
                var deliveryMet = deliveryJudged && judgedDrops.All(x => x.OnTime == true);
                var late = judgedDrops.Where(x => x.OnTime == false && x.PromisedBy is not null && x.CompletedAt is not null).ToList();
                decimal DelayOf(DeliveryFact x) => Math.Max(0m, Minutes(x.CompletedAt, x.PromisedBy) ?? 0m);
                if (!pickupJudged && !deliveryJudged)
                {
                    continue;
                }

                d.Rows.Add(new ReportRow
                {
                    ["shipment"] = s.ShipmentRef, ["transporter"] = s.TransporterName, ["lane"] = s.Lane, ["month"] = Bucket(s.PlannedPickupDate, "month"), ["shipments"] = 1,
                    ["plannedPickup"] = s.PlannedPickupAt, ["actualPickup"] = s.ActualPickupAt, ["pickupDelayMin"] = pickupDelay, ["otp"] = pickupJudged ? Yn(pickupMet) : Yn(null), ["otpMet"] = One(pickupMet), ["otpJudged"] = One(pickupJudged),
                    ["plannedDelivery"] = drops.Where(x => x.PromisedBy is not null).Max(x => x.PromisedBy), ["actualDelivery"] = drops.Where(x => x.CompletedAt is not null).Max(x => x.CompletedAt),
                    ["deliveryDelayMin"] = late.Count > 0 ? late.Max(DelayOf) : deliveryJudged ? 0m : null, ["otd"] = deliveryJudged ? Yn(deliveryMet) : Yn(null), ["otdMet"] = One(deliveryMet), ["otdJudged"] = One(deliveryJudged),
                    ["carrierDelayMin"] = late.Where(x => x.DelayResponsibility == "Carrier").Sum(DelayOf), ["nonCarrierDelayMin"] = late.Where(x => x.DelayResponsibility == "NonCarrier").Sum(DelayOf),
                    ["unattributedDelayMin"] = late.Where(x => x.DelayResponsibility is null).Sum(DelayOf),
                });
            }

            d.Cards.Add(await ctx.CardAsync("OTP"));
            d.Cards.Add(await ctx.CardAsync("OTD"));
            d.Notes.Add("Delay responsibility is a finding made on the delivery, never assumed: a late delivery nobody has judged is shown as 'not yet judged', not charged to the carrier. A shipment with no planned time is not measurable, not late.");
            d.Charts.Add(Chart("delay-split", "Delivery delay by responsibility (minutes)", "stackedBar", "transporter",
                d.Rows.Where(r => r["transporter"] is not null).GroupBy(r => (string)r["transporter"]!).Select(g => new ReportRow
                {
                    ["transporter"] = g.Key, ["carrier"] = g.Sum(r => (decimal)(r["carrierDelayMin"] ?? 0m)), ["nonCarrier"] = g.Sum(r => (decimal)(r["nonCarrierDelayMin"] ?? 0m)), ["notJudged"] = g.Sum(r => (decimal)(r["unattributedDelayMin"] ?? 0m)),
                }).OrderByDescending(r => (decimal)r["carrier"]!).Take(ctx.Settings.TopN * 2),
                new ChartSeries("carrier", "Carrier-attributable"), new ChartSeries("nonCarrier", "Non-carrier"), new ChartSeries("notJudged", "Not yet judged")));
            return d;
        },
    };

    private static ReportSpec PodCompliance() => new()
    {
        Code = "R15_POD_COMPLIANCE_BY_TRANSPORTER", Name = "POD compliance by transporter", Category = CatTransporters, Type = ReportType.Analytical, DataSource = "Deliveries (POD)", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Transporters, VendorSafe = true, SortOrder = 150, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "For each transporter: deliveries, proofs required, submitted, within SLA, rejected and accepted, compliance and the time taken to submit.",
        Columns = Cols(
            C("transporter", "Transporter", filter: true), C("month", "Month"), C("customer", "Customer", filter: true), C("deliveries", "Deliveries", FieldType.Whole), C("required", "POD required", FieldType.Whole),
            C("submitted", "POD submitted", FieldType.Whole), C("withinSla", "Within SLA", FieldType.Whole), C("judged", "Judged", FieldType.Whole, visible: false), C("rejected", "Rejected", FieldType.Whole), C("accepted", "Accepted", FieldType.Whole),
            C("compliance", "Compliance %", FieldType.Percent), C("submissionHours", "Avg submission time (h)", FieldType.Number), C("pod", "POD"), C("shipment", "Shipment")),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Customer, FilterNames.Status),
        Groupings = Groups(("transporter", "Transporter"), ("customer", "Customer"), ("month", "Month")),
        DefaultGroupBy = ["transporter"],
        Measures =
        [
            new("deliveries", MeasureKind.Count), new("required", MeasureKind.Sum), new("submitted", MeasureKind.Sum), new("withinSla", MeasureKind.Sum), new("judged", MeasureKind.Sum), new("rejected", MeasureKind.Sum), new("accepted", MeasureKind.Sum),
            new("compliance", MeasureKind.Ratio, "withinSla", "judged", 100m), new("submissionHours", MeasureKind.Avg),
        ],
        Drills = [new("transporter", "R19_POD_AGEING", "Open proofs of this transporter", Map((FilterNames.Transporter, "transporter"))), new("rejected", "R20_POD_REJECTIONS", "Rejections", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("compliance"),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            foreach (var p in await ctx.Facts.Pods())
            {
                var judged = p.Required && p.SubmittedWithinSla is not null;
                d.Rows.Add(new ReportRow
                {
                    ["transporter"] = p.TransporterName, ["month"] = Bucket(p.Date, "month"), ["customer"] = p.Customer, ["deliveries"] = 1, ["required"] = One(p.Required), ["submitted"] = One(p.Required && p.SubmittedAt is not null),
                    ["withinSla"] = One(judged && p.SubmittedWithinSla == true), ["judged"] = One(judged), ["rejected"] = One(p.RejectedAt is not null || p.Status == "Rejected"), ["accepted"] = One(p.Status == "Accepted"),
                    ["submissionHours"] = p.SubmittedAt is not null && p.DeliveryCompletedAt is not null ? Math.Round((decimal)(p.SubmittedAt.Value - p.DeliveryCompletedAt.Value).TotalHours, 1) : null, ["pod"] = p.PodRef, ["shipment"] = p.ShipmentRef,
                });
            }

            d.Cards.Add(await ctx.CardAsync("POD_COMPLIANCE"));
            d.Notes.Add("Deliveries where no proof is required are not applicable and are left out of the compliance figure; a proof not yet due is not counted as late.");
            return d;
        },
    };

    private static ReportSpec Exceptions() => new()
    {
        Code = "R16_TRANSPORTER_EXCEPTIONS", Name = "Transporter exceptions", Category = CatTransporters, Type = ReportType.Operational, DataSource = "Deliveries + Tracking + Transporters", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Transporters, VendorSafe = true, SortOrder = 160,
        Description = "Exceptions raised against a transporter by any module: type, severity, shipment, lane, age, owner and status.",
        Columns = Cols(
            C("transporter", "Transporter", filter: true), C("module", "Raised by", FieldType.Status), C("exception", "Exception", filter: true), C("severity", "Severity", FieldType.Status, filter: true), C("shipment", "Shipment"), C("lane", "Lane", filter: true),
            C("createdAt", "Created", FieldType.DateTime), C("ageHours", "Age (h)", FieldType.Whole), C("owner", "Owner"), C("status", "Status", FieldType.Status, filter: true), C("reference", "Reference"), C("count", "Exceptions", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Lane, FilterNames.Exception, "severity", FilterNames.Status),
        Groupings = Groups(("transporter", "Transporter"), ("severity", "Severity"), ("exception", "Exception"), ("module", "Raised by")),
        Measures = [new("count", MeasureKind.Count)],
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("createdAt", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var severity = ctx.Filters.Get("severity");
            var open = (await ctx.Facts.Exceptions()).Where(e => e.TransporterId is not null).ToList();
            var rows = open.Where(e => severity is null || e.Severity.Equals(severity, StringComparison.OrdinalIgnoreCase)).ToList();
            var now = ctx.Now;
            foreach (var e in rows)
            {
                var end = e.ResolvedAt ?? now;
                d.Rows.Add(new ReportRow
                {
                    ["transporter"] = e.TransporterName, ["module"] = e.Module, ["exception"] = e.Type, ["severity"] = e.Severity, ["shipment"] = e.ShipmentRef, ["lane"] = e.Lane, ["createdAt"] = e.CreatedAt,
                    ["ageHours"] = (int)(end - e.CreatedAt).TotalHours, ["owner"] = e.Owner, ["status"] = e.Status, ["reference"] = e.ExceptionRef, ["count"] = 1,
                });
            }

            d.Totals.Add(new("open", "Open", rows.Count(e => e.Status != "Resolved")));
            d.Totals.Add(new("critical", "Open critical", rows.Count(e => e.Status != "Resolved" && e.Severity == "Critical"), null, null, null, rows.Any(e => e.Status != "Resolved" && e.Severity == "Critical") ? "bad" : null));
            d.Totals.Add(new("resolved", "Resolved", rows.Count(e => e.Status == "Resolved")));
            d.Charts.Add(Chart("by-transporter", "Exceptions by transporter and severity", "stackedBar", "transporter",
                rows.Where(e => e.TransporterName is not null).GroupBy(e => e.TransporterName!).Select(g => new ReportRow
                {
                    ["transporter"] = g.Key, ["Critical"] = g.Count(e => e.Severity == "Critical"), ["High"] = g.Count(e => e.Severity == "High"), ["Warning"] = g.Count(e => e.Severity == "Warning"), ["Info"] = g.Count(e => e.Severity == "Info"),
                }).OrderByDescending(r => (int)r["Critical"]! * 1000 + (int)r["High"]!).Take(ctx.Settings.TopN * 2),
                new ChartSeries("Critical", "Critical", "#dc2626"), new ChartSeries("High", "High", "#f97316"), new ChartSeries("Warning", "Warning", "#eab308"), new ChartSeries("Info", "Info", "#3b82f6")));
            return d;
        },
    };
}
