using Tms.SharedKernel.Contracts;
using static Tms.Modules.Reports.Domain.Kit;

namespace Tms.Modules.Reports.Domain;

/// <summary>R24–R30: shipment tracking and visibility. Health, risk, ETA and deviations are Tracking's own findings; these reports present them.</summary>
internal static class TrackingReports
{
    public static IEnumerable<ReportSpec> All()
    {
        yield return ControlTower();
        yield return EtaDelay();
        yield return RouteDeviation();
        yield return Dwell();
        yield return TrackingHealth();
        yield return TrackingGaps();
        yield return PlannedVsActualRoute();
    }

    private static ReportSpec ControlTower() => new()
    {
        Code = "R24_CONTROL_TOWER", Name = "Live control tower", Category = CatTracking, Type = ReportType.Dashboard, DataSource = "Tracking", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Tracking, SortOrder = 240,
        Description = "Everything on the road now: on time, at risk, delayed, tracking stale or lost, deviations, excess dwell and open exceptions, on a live map.",
        Columns = Cols(
            C("trip", "Trip"), C("shipment", "Shipment", filter: true), C("vehicle", "Vehicle"), C("transporter", "Transporter", filter: true), C("lane", "Lane", filter: true), C("health", "Tracking", FieldType.Status, filter: true), C("risk", "Delivery risk", FieldType.Status, filter: true),
            C("latestEta", "Latest ETA", FieldType.DateTime), C("plannedEta", "Planned ETA", FieldType.DateTime), C("lastSeenAt", "Last seen", FieldType.DateTime), C("deviationKm", "Deviation (km)", FieldType.Number), C("openExceptions", "Open exceptions", FieldType.Whole)),
        Filters = Filters(FilterNames.Transporter, FilterNames.Lane, FilterNames.Region, FilterNames.Status),
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("risk", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var active = (await ctx.Facts.Trips(anyDate: true)).Where(t => t.Execution == "InTransit").ToList();
            var openExceptions = (await ctx.Facts.Exceptions()).Where(e => e.Status != "Resolved").ToList();
            var dwell = (await ctx.Facts.Dwells()).Where(w => w.ActualMinutes > w.ExpectedMinutes).Select(w => w.TripRef).ToHashSet(StringComparer.Ordinal);
            string Rank(string risk) => risk switch { "SeverelyDelayed" => "0", "Delayed" => "1", "AtRisk" => "2", _ => "3" };
            foreach (var t in active.OrderBy(t => Rank(t.Risk)).ThenBy(t => t.LatestEta))
            {
                d.Rows.Add(new ReportRow
                {
                    ["trip"] = t.TripRef, ["shipment"] = t.ShipmentRef, ["vehicle"] = t.VehicleRef, ["transporter"] = t.TransporterName, ["lane"] = t.Lane, ["health"] = t.Health, ["risk"] = t.Risk, ["latestEta"] = t.LatestEta,
                    ["plannedEta"] = t.PlannedEta, ["lastSeenAt"] = t.LastSeenAt, ["deviationKm"] = t.DeviationKm, ["openExceptions"] = t.OpenExceptions,
                });
            }

            var drill = (string key, string value) => (IReadOnlyDictionary<string, string>)new Dictionary<string, string> { [key] = value };
            d.Totals.Add(new("active", "Active shipments", active.Count));
            d.Totals.Add(new("onTime", "On time", active.Count(t => t.Risk == "OnTime"), null, "R25_ETA_DELAY", drill("status", "OnTime"), "good"));
            d.Totals.Add(new("atRisk", "At risk", active.Count(t => t.Risk == "AtRisk"), null, "R25_ETA_DELAY", drill("status", "AtRisk"), active.Any(t => t.Risk == "AtRisk") ? "warn" : null));
            d.Totals.Add(new("delayed", "Delayed", active.Count(t => t.Risk is "Delayed" or "SeverelyDelayed"), null, "R25_ETA_DELAY", drill("status", "Delayed"), active.Any(t => t.Risk is "Delayed" or "SeverelyDelayed") ? "bad" : null));
            d.Totals.Add(new("stale", "Tracking stale", active.Count(t => t.Health == "Stale"), null, "R28_TRACKING_HEALTH", drill("status", "Stale"), active.Any(t => t.Health == "Stale") ? "warn" : null));
            d.Totals.Add(new("lost", "Tracking lost", active.Count(t => t.Health == "Lost"), null, "R28_TRACKING_HEALTH", drill("status", "Lost"), active.Any(t => t.Health == "Lost") ? "bad" : null));
            d.Totals.Add(new("deviations", "Route deviations", active.Count(t => t.DeviationKm > 0), null, "R26_ROUTE_DEVIATION", null));
            d.Totals.Add(new("dwell", "Excess dwell", active.Count(t => dwell.Contains(t.TripRef)), null, "R27_DWELL_TIME", null));
            d.Totals.Add(new("exceptions", "Open exceptions", openExceptions.Count(e => active.Any(t => t.ShipmentRef == e.ShipmentRef)), null, "R16_TRANSPORTER_EXCEPTIONS", null));
            d.Charts.Add(new ChartData("map", "Vehicles on the road", "map", null, [new ChartSeries("risk", "Delivery risk")],
                active.Where(t => t.LastLatitude is not null && t.LastLongitude is not null).Select(t => new ReportRow
                {
                    ["lat"] = t.LastLatitude, ["lon"] = t.LastLongitude, ["label"] = $"{t.ShipmentRef} · {t.VehicleRef} · {t.Lane}", ["risk"] = t.Risk, ["health"] = t.Health, ["shipment"] = t.ShipmentRef,
                }).ToList(), "Marker colour is the delivery risk; a grey marker is a vehicle whose tracking is stale or lost (its last known place)."));
            d.Fixed = true;
            return d;
        },
    };

    private static ReportSpec EtaDelay() => new()
    {
        Code = "R25_ETA_DELAY", Name = "ETA / delay report", Category = CatTracking, Type = ReportType.Analytical, DataSource = "Tracking", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Tracking, SortOrder = 250, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Planned and latest ETA against the actual arrival: how wrong the estimates were, how late trips are and which are at risk.",
        Columns = Cols(
            C("shipment", "Shipment", filter: true), C("transporter", "Transporter", filter: true), C("lane", "Lane", filter: true), C("month", "Month"), C("risk", "Risk", FieldType.Status, filter: true), C("plannedEta", "Planned ETA", FieldType.DateTime),
            C("latestEta", "Latest ETA", FieldType.DateTime), C("actualArrival", "Actual arrival", FieldType.DateTime), C("etaVarianceMin", "ETA variance (min)", FieldType.Whole), C("etaErrorMin", "ETA error (min)", FieldType.Whole), C("delayMin", "Delay (min)", FieldType.Whole),
            C("trips", "Trips", FieldType.Whole), C("accurate", "Within tolerance", FieldType.Whole, visible: false), C("judged", "ETA judged", FieldType.Whole, visible: false), C("etaAccuracy", "ETA accuracy %", FieldType.Percent),
            C("atRisk", "At risk", FieldType.Whole), C("delayed", "Delayed", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Lane, FilterNames.Region, FilterNames.Shipment, FilterNames.Status).Select(f => f.Name == FilterNames.Status ? f.With("risk") : f).ToList(),
        Groupings = Groups(("transporter", "Transporter"), ("lane", "Lane"), ("month", "Month"), ("risk", "Risk")),
        Measures =
        [
            new("trips", MeasureKind.Count), new("accurate", MeasureKind.Sum), new("judged", MeasureKind.Sum), new("etaAccuracy", MeasureKind.Ratio, "accurate", "judged", 100m), new("etaErrorMin", MeasureKind.Avg),
            new("delayMin", MeasureKind.Avg), new("atRisk", MeasureKind.Sum), new("delayed", MeasureKind.Sum),
        ],
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment"))), new("atRisk", "R24_CONTROL_TOWER", "Control tower", new Dictionary<string, string>())],
        Sorts = Sort("delayMin", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var trips = (await ctx.Facts.Trips()).Where(t => t.PlannedEta is not null || t.LatestEta is not null).ToList();
            var errors = new List<decimal>();
            foreach (var t in trips)
            {
                var judged = t.ActualArrival is not null && t.LatestEta is not null;
                var err = judged ? (decimal?)Math.Abs((t.ActualArrival!.Value - t.LatestEta!.Value).TotalMinutes) : null;
                if (err is not null)
                {
                    errors.Add(Math.Round(err.Value, 0));
                }

                var basis = t.ActualArrival ?? t.LatestEta;
                d.Rows.Add(new ReportRow
                {
                    ["shipment"] = t.ShipmentRef, ["transporter"] = t.TransporterName, ["lane"] = t.Lane, ["month"] = Bucket(t.Date, "month"), ["risk"] = t.Risk, ["plannedEta"] = t.PlannedEta, ["latestEta"] = t.LatestEta,
                    ["actualArrival"] = t.ActualArrival, ["etaVarianceMin"] = Minutes(t.LatestEta, t.PlannedEta), ["etaErrorMin"] = err is null ? null : Math.Round(err.Value, 0), ["delayMin"] = Minutes(basis, t.PlannedEta),
                    ["trips"] = 1, ["accurate"] = One(err is not null && err <= ctx.Settings.EtaToleranceMinutes), ["judged"] = One(judged), ["atRisk"] = One(t.Risk == "AtRisk"), ["delayed"] = One(t.Risk is "Delayed" or "SeverelyDelayed"),
                });
            }

            d.Cards.Add(await ctx.CardAsync("ETA_ACCURACY"));
            d.Cards.Add(await ctx.CardAsync("ETA_ERROR_MIN"));
            d.Totals.Add(new("median", "Median ETA error (min)", Median(errors), "number"));
            d.Totals.Add(new("atRisk", "At-risk shipments", trips.Count(t => t.Risk == "AtRisk")));
            d.Totals.Add(new("delayed", "Delayed shipments", trips.Count(t => t.Risk is "Delayed" or "SeverelyDelayed")));
            (string Label, Func<decimal, bool> In)[] bands = [("0–15 min", e => e <= 15), ("16–30 min", e => e is > 15 and <= 30), ("31–60 min", e => e is > 30 and <= 60), ("61–120 min", e => e is > 60 and <= 120), (">120 min", e => e > 120)];
            d.Charts.Add(Chart("error-bands", "How far the last ETA was from the actual arrival", "bar", "band", bands.Select(b => new ReportRow { ["band"] = b.Label, ["trips"] = errors.Count(b.In) }), new ChartSeries("trips", "Trips")));
            d.Notes.Add($"A trip counts as accurate when the last ETA was within {ctx.Settings.EtaToleranceMinutes} minutes of the actual arrival. Trips still on the road have no actual arrival and are not judged.");
            return d;
        },
    };

    private static ReportSpec RouteDeviation() => new()
    {
        Code = "R26_ROUTE_DEVIATION", Name = "Route deviation report", Category = CatTracking, Type = ReportType.Operational, DataSource = "Tracking", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Tracking, SortOrder = 260, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Where vehicles left their planned route: how far, for how long, when, why and whether it is still open.",
        Columns = Cols(
            C("shipment", "Shipment", filter: true), C("vehicle", "Vehicle", filter: true), C("transporter", "Transporter", filter: true), C("route", "Route"), C("deviationKm", "Deviation (km)", FieldType.Number), C("durationMin", "Duration (min)", FieldType.Whole),
            C("detectedAt", "Detected at", FieldType.DateTime), C("reason", "Reason"), C("status", "Status", FieldType.Status, filter: true), C("deviations", "Deviations", FieldType.Whole), C("month", "Month")),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Vehicle, FilterNames.Shipment, FilterNames.Status),
        Groupings = Groups(("transporter", "Transporter"), ("status", "Status"), ("month", "Month")),
        Measures = [new("deviations", MeasureKind.Count), new("deviationKm", MeasureKind.Sum), new("durationMin", MeasureKind.Avg)],
        Drills = [new("shipment", "R30_PLANNED_VS_ACTUAL_ROUTE", "Planned vs actual route", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("detectedAt", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rows = await ctx.Facts.Deviations();
            foreach (var x in rows)
            {
                d.Rows.Add(new ReportRow
                {
                    ["shipment"] = x.ShipmentRef, ["vehicle"] = x.VehicleRef, ["transporter"] = x.TransporterName, ["route"] = x.Route, ["deviationKm"] = x.DeviationKm, ["durationMin"] = x.DurationMin, ["detectedAt"] = x.DetectedAt,
                    ["reason"] = x.Reason, ["status"] = x.Status, ["deviations"] = 1, ["month"] = Bucket(Day(x.DetectedAt), "month"),
                });
            }

            d.Cards.Add(await ctx.CardAsync("ROUTE_DEVIATIONS"));
            d.Totals.Add(new("total", "Total deviation (km)", rows.Sum(r => r.DeviationKm), "number"));
            d.Totals.Add(new("avg", "Average deviation (km)", rows.Count == 0 ? null : Math.Round(rows.Average(r => r.DeviationKm), 1), "number"));
            d.Totals.Add(new("open", "Still open", rows.Count(r => r.Status is "Open" or "Ongoing")));
            d.Charts.Add(Chart("by-transporter", "Deviation kilometres by transporter", "bar", "transporter",
                rows.Where(r => r.TransporterName is not null).GroupBy(r => r.TransporterName!).OrderByDescending(g => g.Sum(r => r.DeviationKm)).Take(ctx.Settings.TopN).Select(g => new ReportRow { ["transporter"] = g.Key, ["km"] = g.Sum(r => r.DeviationKm) }), new ChartSeries("km", "Deviation km")));
            return d;
        },
    };

    private static ReportSpec Dwell() => new()
    {
        Code = "R27_DWELL_TIME", Name = "Dwell time report", Category = CatTracking, Type = ReportType.Analytical, DataSource = "Tracking", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Tracking, SortOrder = 270, ExportFormats = "csv,xlsx,pdf",
        Description = "Time vehicles stood at origin, customer, hub and unplanned places: expected against actual, and the excess.",
        Columns = Cols(
            C("kind", "Dwell type", FieldType.Status, filter: true), C("place", "Place"), C("shipment", "Shipment", filter: true), C("trip", "Trip"), C("at", "When", FieldType.DateTime), C("month", "Month"),
            C("events", "Stops", FieldType.Whole), C("expectedMin", "Expected (min)", FieldType.Number), C("actualMin", "Actual (min)", FieldType.Number), C("excessMin", "Excess (min)", FieldType.Number), C("excessEvents", "Stops over expected", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Shipment, FilterNames.Status).Select(f => f.Name == FilterNames.Status ? f.With("dwellKind") : f).ToList(),
        Groupings = Groups(("kind", "Dwell type"), ("month", "Month"), ("place", "Place")),
        DefaultGroupBy = ["kind"],
        Measures = [new("events", MeasureKind.Count), new("expectedMin", MeasureKind.Avg), new("actualMin", MeasureKind.Avg), new("excessMin", MeasureKind.Sum), new("excessEvents", MeasureKind.Sum)],
        Drills = [new("shipment", "R30_PLANNED_VS_ACTUAL_ROUTE", "Planned vs actual route", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("excessMin", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rows = await ctx.Facts.Dwells();
            foreach (var x in rows)
            {
                var excess = Math.Max(0, x.ActualMinutes - x.ExpectedMinutes);
                d.Rows.Add(new ReportRow
                {
                    ["kind"] = x.Kind, ["place"] = x.Place, ["shipment"] = x.ShipmentRef, ["trip"] = x.TripRef, ["at"] = x.At, ["month"] = Bucket(Day(x.At), "month"), ["events"] = 1,
                    ["expectedMin"] = x.ExpectedMinutes, ["actualMin"] = x.ActualMinutes, ["excessMin"] = excess, ["excessEvents"] = One(excess > 0),
                });
            }

            d.Totals.Add(new("stops", "Stops", rows.Count));
            d.Totals.Add(new("excess", "Stops over expected", rows.Count(x => x.ActualMinutes > x.ExpectedMinutes)));
            d.Totals.Add(new("excessMin", "Excess dwell (min)", rows.Sum(x => Math.Max(0, x.ActualMinutes - x.ExpectedMinutes)), "number"));
            d.Charts.Add(Chart("by-kind", "Expected and actual dwell by type (average minutes)", "bar", "kind",
                rows.GroupBy(x => x.Kind).Select(g => new ReportRow { ["kind"] = g.Key, ["expected"] = Math.Round(g.Average(x => (decimal)x.ExpectedMinutes), 0), ["actual"] = Math.Round(g.Average(x => (decimal)x.ActualMinutes), 0) }),
                new ChartSeries("expected", "Expected"), new ChartSeries("actual", "Actual")));
            d.Notes.Add("An unplanned stop has no expected time, so its whole duration counts as excess.");
            return d;
        },
    };

    private static ReportSpec TrackingHealth() => new()
    {
        Code = "R28_TRACKING_HEALTH", Name = "Tracking health", Category = CatTracking, Type = ReportType.Analytical, DataSource = "Tracking", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Tracking, SortOrder = 280, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Is the phone reporting? Tracking sessions that are healthy, stale, lost, not started or completed, and tracking coverage.",
        Columns = Cols(
            C("trip", "Trip", filter: true), C("shipment", "Shipment"), C("transporter", "Transporter", filter: true), C("lane", "Lane", filter: true), C("vehicle", "Vehicle"), C("month", "Month"), C("health", "Tracking health", FieldType.Status, filter: true),
            C("lastSeenAt", "Last seen", FieldType.DateTime), C("sessions", "Tracking sessions", FieldType.Whole), C("healthy", "Healthy", FieldType.Whole), C("stale", "Stale", FieldType.Whole), C("lost", "Lost", FieldType.Whole),
            C("notStarted", "Not started", FieldType.Whole), C("completed", "Completed", FieldType.Whole), C("started", "Tracking started", FieldType.Whole, visible: false), C("coverage", "Tracking coverage %", FieldType.Percent)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Lane, FilterNames.Region, FilterNames.Status).Select(f => f.Name == FilterNames.Status ? f.With("trackingHealth") : f).ToList(),
        Groupings = Groups(("transporter", "Transporter"), ("health", "Tracking health"), ("lane", "Lane"), ("month", "Month")),
        DefaultGroupBy = ["transporter"],
        Measures =
        [
            new("sessions", MeasureKind.Count), new("healthy", MeasureKind.Sum), new("stale", MeasureKind.Sum), new("lost", MeasureKind.Sum), new("notStarted", MeasureKind.Sum), new("completed", MeasureKind.Sum),
            new("started", MeasureKind.Sum), new("coverage", MeasureKind.Ratio, "started", "sessions", 100m),
        ],
        Drills = [new("lost", "R29_TRACKING_GAPS", "Tracking gaps", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("coverage"),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var trips = (await ctx.Facts.Trips()).Where(t => t.Execution != "Cancelled").ToList();
            foreach (var t in trips)
            {
                d.Rows.Add(new ReportRow
                {
                    ["trip"] = t.TripRef, ["shipment"] = t.ShipmentRef, ["transporter"] = t.TransporterName, ["lane"] = t.Lane, ["vehicle"] = t.VehicleRef, ["month"] = Bucket(t.Date, "month"), ["health"] = t.Health, ["lastSeenAt"] = t.LastSeenAt,
                    ["sessions"] = 1, ["healthy"] = One(t.Health == "Healthy"), ["stale"] = One(t.Health == "Stale"), ["lost"] = One(t.Health == "Lost"), ["notStarted"] = One(t.Health == "NotStarted"), ["completed"] = One(t.Health == "Completed" || t.Execution == "Completed"),
                    ["started"] = One(t.Health != "NotStarted"),
                });
            }

            d.Cards.Add(await ctx.CardAsync("TRACKING_COVERAGE"));
            d.Cards.Add(await ctx.CardAsync("TRACKING_LOST"));
            d.Charts.Add(Chart("health", "Tracking sessions by health", "donut", "health", trips.GroupBy(t => t.Health).Select(g => new ReportRow { ["health"] = g.Key, ["sessions"] = g.Count() }), new ChartSeries("sessions", "Sessions")));
            d.Notes.Add("A trip whose phone stopped reporting is 'stale' or 'lost', never reported as parked.");
            return d;
        },
    };

    private static ReportSpec TrackingGaps() => new()
    {
        Code = "R29_TRACKING_GAPS", Name = "Tracking gap report", Category = CatTracking, Type = ReportType.Operational, DataSource = "Tracking", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Tracking, SortOrder = 290,
        Description = "Periods when a vehicle stopped reporting: where it was last seen, how long the gap lasted and how serious it is.",
        Columns = Cols(
            C("shipment", "Shipment", filter: true), C("vehicle", "Vehicle", filter: true), C("transporter", "Transporter", filter: true), C("lastKnownPlace", "Last known location"), C("gapStart", "Gap start", FieldType.DateTime), C("gapEnd", "Gap end", FieldType.DateTime),
            C("durationMin", "Gap duration (min)", FieldType.Whole), C("severity", "Severity", FieldType.Status, filter: true), C("status", "Status", FieldType.Status, filter: true), C("gaps", "Gaps", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Vehicle, FilterNames.Shipment, FilterNames.Status),
        Groupings = Groups(("transporter", "Transporter"), ("severity", "Severity"), ("status", "Status")),
        Measures = [new("gaps", MeasureKind.Count), new("durationMin", MeasureKind.Sum)],
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("gapStart", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rows = await ctx.Facts.Gaps();
            foreach (var g in rows)
            {
                d.Rows.Add(new ReportRow
                {
                    ["shipment"] = g.ShipmentRef, ["vehicle"] = g.VehicleRef, ["transporter"] = g.TransporterName, ["lastKnownPlace"] = g.LastKnownPlace, ["gapStart"] = g.GapStart, ["gapEnd"] = g.GapEnd,
                    ["durationMin"] = g.DurationMin, ["severity"] = g.Severity, ["status"] = g.Status, ["gaps"] = 1,
                });
            }

            d.Totals.Add(new("gaps", "Gaps", rows.Count));
            d.Totals.Add(new("open", "Still open", rows.Count(g => g.GapEnd is null)));
            d.Totals.Add(new("minutes", "Total gap time (min)", rows.Sum(g => g.DurationMin), "number"));
            d.Charts.Add(Chart("by-severity", "Gaps by severity", "donut", "severity", rows.GroupBy(g => g.Severity).Select(g => new ReportRow { ["severity"] = g.Key, ["gaps"] = g.Count() }), new ChartSeries("gaps", "Gaps")));
            return d;
        },
    };

    private static ReportSpec PlannedVsActualRoute() => new()
    {
        Code = "R30_PLANNED_VS_ACTUAL_ROUTE", Name = "Planned vs actual route", Category = CatTracking, Type = ReportType.Operational, DataSource = "Tracking", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Tracking, SortOrder = 300, ExportFormats = "csv,xlsx,pdf",
        Description = "Planned distance, duration and stops against what happened, with dwell and deviation. The map draws the planned and the driven route.",
        Columns = Cols(
            C("shipment", "Shipment", filter: true), C("trip", "Trip"), C("transporter", "Transporter", filter: true), C("lane", "Lane", filter: true), C("plannedDistance", "Planned km", FieldType.Number), C("actualDistance", "Actual km", FieldType.Number),
            C("distanceVariance", "Distance variance (km)", FieldType.Number), C("plannedDuration", "Planned duration (min)", FieldType.Whole), C("actualDuration", "Actual duration (min)", FieldType.Whole), C("durationVariance", "Duration variance (min)", FieldType.Whole),
            C("plannedStops", "Planned stops", FieldType.Whole), C("actualStops", "Actual stops", FieldType.Whole), C("unplannedStops", "Unplanned stops", FieldType.Whole), C("dwellMin", "Dwell (min)", FieldType.Whole), C("deviationKm", "Deviation (km)", FieldType.Number)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Shipment, FilterNames.Trip, FilterNames.Transporter, FilterNames.Lane, FilterNames.Region),
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("deviationKm", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var trips = (await ctx.Facts.Trips()).Where(t => t.Execution != "Cancelled").ToList();
            foreach (var t in trips)
            {
                d.Rows.Add(new ReportRow
                {
                    ["shipment"] = t.ShipmentRef, ["trip"] = t.TripRef, ["transporter"] = t.TransporterName, ["lane"] = t.Lane, ["plannedDistance"] = t.PlannedDistanceKm, ["actualDistance"] = t.ActualDistanceKm,
                    ["distanceVariance"] = t.PlannedDistanceKm is { } p && t.ActualDistanceKm is { } a ? a - p : null, ["plannedDuration"] = t.PlannedDurationMin, ["actualDuration"] = t.ActualDurationMin,
                    ["durationVariance"] = t.PlannedDurationMin is { } pm && t.ActualDurationMin is { } am ? am - pm : null, ["plannedStops"] = t.PlannedStops, ["actualStops"] = t.ActualStops, ["unplannedStops"] = t.UnplannedStops,
                    ["dwellMin"] = t.DwellMinutes, ["deviationKm"] = t.DeviationKm,
                });
            }

            var mapped = (ctx.Filters.Get(FilterNames.Shipment) is not null || ctx.Filters.Get(FilterNames.Trip) is not null ? trips : trips.OrderByDescending(t => t.DeviationKm).Take(3).ToList()).Where(t => t.PlannedRoute is not null || t.ActualRoute is not null).Take(5).ToList();
            var points = new List<ReportRow>();
            foreach (var t in mapped)
            {
                var i = 0;
                points.AddRange((t.PlannedRoute ?? []).Select(p => new ReportRow { ["trip"] = t.TripRef, ["shipment"] = t.ShipmentRef, ["kind"] = "planned", ["seq"] = i++, ["lat"] = p.Latitude, ["lon"] = p.Longitude }));
                i = 0;
                points.AddRange((t.ActualRoute ?? []).Select(p => new ReportRow { ["trip"] = t.TripRef, ["shipment"] = t.ShipmentRef, ["kind"] = "actual", ["seq"] = i++, ["lat"] = p.Latitude, ["lon"] = p.Longitude }));
            }

            if (points.Count > 0)
            {
                d.Charts.Add(new ChartData("route-map", "Planned route (blue) and driven route (orange)", "map", null, [new ChartSeries("kind", "Route")], points, mapped.Count > 1 ? "The three trips with the most deviation, or the trips you filtered to." : null));
            }

            d.Totals.Add(new("trips", "Trips", trips.Count));
            d.Totals.Add(new("km", "Total deviation (km)", trips.Sum(t => t.DeviationKm), "number"));
            d.Totals.Add(new("unplanned", "Unplanned stops", trips.Sum(t => t.UnplannedStops)));
            return d;
        },
    };
}
