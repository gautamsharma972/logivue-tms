using Tms.SharedKernel.Contracts;
using static Tms.Modules.Reports.Domain.Kit;

namespace Tms.Modules.Reports.Domain;

/// <summary>R02–R08: planning and load optimisation. All figures are Planning's own (plans, vehicles, savings); these reports only list and total them.</summary>
internal static class PlanningReports
{
    public static IEnumerable<ReportSpec> All()
    {
        yield return LoadPlanningSummary();
        yield return VehicleAllocation();
        yield return VehicleUtilisation();
        yield return Unplanned();
        yield return FtlVsPtl();
        yield return Consolidation();
        yield return PlannedVsActualTrip();
    }

    private static ReportSpec LoadPlanningSummary() => new()
    {
        Code = "R02_LOAD_PLANNING_SUMMARY", Name = "Load planning summary", Category = CatPlanning, Type = ReportType.Analytical, DataSource = "Planning", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Planning, SortOrder = 20, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Planning runs: orders selected, planned and left out, vehicles used, distance, estimated cost and utilisation. Open a run to see its vehicles.",
        Columns = Cols(
            C("run", "Planning run", filter: true), C("planningDate", "Planning date", FieldType.Date), C("month", "Month"), C("version", "Version", FieldType.Whole), C("status", "Status", FieldType.Status, filter: true),
            C("ordersSelected", "Orders selected", FieldType.Whole), C("ordersPlanned", "Orders planned", FieldType.Whole), C("ordersUnplanned", "Orders unplanned", FieldType.Whole),
            C("vehiclesUsed", "Vehicles used", FieldType.Whole), C("distanceKm", "Total distance (km)", FieldType.Number), C("estimatedCost", "Estimated cost", FieldType.Currency),
            C("weightUtilisation", "Avg weight utilisation", FieldType.Percent), C("volumeUtilisation", "Avg volume utilisation", FieldType.Percent), C("runs", "Planning runs", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Status, FilterNames.Load),
        Groupings = Groups(("month", "Month"), ("status", "Status"), ("planningDate", "Date")),
        Measures =
        [
            new("runs", MeasureKind.Count), new("ordersSelected", MeasureKind.Sum), new("ordersPlanned", MeasureKind.Sum), new("ordersUnplanned", MeasureKind.Sum), new("vehiclesUsed", MeasureKind.Sum),
            new("distanceKm", MeasureKind.Sum), new("estimatedCost", MeasureKind.Sum), new("weightUtilisation", MeasureKind.Avg), new("volumeUtilisation", MeasureKind.Avg),
        ],
        Drills = [new("run", "R03_VEHICLE_ALLOCATION", "Vehicles of this run", Map((FilterNames.Load, "run"))), new("ordersUnplanned", "R05_UNPLANNED", "Unplanned orders of this run", Map((FilterNames.Load, "run")))],
        Sorts = Sort("planningDate", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var runs = await ctx.Facts.Runs();
            foreach (var r in runs.OrderByDescending(r => r.PlanningDate))
            {
                d.Rows.Add(new ReportRow
                {
                    ["run"] = r.RunRef, ["planningDate"] = r.PlanningDate, ["month"] = Bucket(r.PlanningDate, "month"), ["version"] = r.Version, ["status"] = r.Status,
                    ["ordersSelected"] = r.OrdersSelected, ["ordersPlanned"] = r.OrdersPlanned, ["ordersUnplanned"] = r.OrdersUnplanned, ["vehiclesUsed"] = r.VehiclesUsed,
                    ["distanceKm"] = r.TotalDistanceKm, ["estimatedCost"] = r.EstimatedCost, ["weightUtilisation"] = r.AvgWeightUtilisationPct, ["volumeUtilisation"] = r.AvgVolumeUtilisationPct,
                });
            }

            d.Totals.Add(new("runs", "Planning runs", runs.Count));
            d.Totals.Add(new("selected", "Orders selected", runs.Sum(r => r.OrdersSelected)));
            d.Totals.Add(new("planned", "Orders planned", runs.Sum(r => r.OrdersPlanned)));
            d.Totals.Add(new("unplanned", "Orders unplanned", runs.Sum(r => r.OrdersUnplanned), null, "R05_UNPLANNED", null, runs.Sum(r => r.OrdersUnplanned) > 0 ? "warn" : null));
            d.Totals.Add(new("vehicles", "Vehicles used", runs.Sum(r => r.VehiclesUsed)));
            d.Totals.Add(new("distance", "Total distance (km)", runs.Sum(r => r.TotalDistanceKm), "number"));
            d.Totals.Add(new("cost", "Total estimated cost", runs.Sum(r => r.EstimatedCost), "currency"));
            d.Cards.Add(await ctx.CardAsync("WEIGHT_UTIL"));
            d.Cards.Add(await ctx.CardAsync("VOLUME_UTIL"));
            d.Cards.Add(await ctx.CardAsync("PLANNING_SAVINGS"));
            d.Charts.Add(Chart("cost-by-month", "Estimated cost and vehicles by month", "bar", "month",
                runs.GroupBy(r => Bucket(r.PlanningDate, "month")).OrderBy(g => g.Key).Select(g => new ReportRow { ["month"] = g.Key, ["cost"] = g.Sum(r => r.EstimatedCost), ["vehicles"] = g.Sum(r => r.VehiclesUsed) }),
                new ChartSeries("cost", "Estimated cost")));
            return d;
        },
    };

    private static ReportSpec VehicleAllocation() => new()
    {
        Code = "R03_VEHICLE_ALLOCATION", Name = "Vehicle allocation", Category = CatPlanning, Type = ReportType.Operational, DataSource = "Planning", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Planning, SortOrder = 30,
        Description = "Which vehicle carries which orders in each planning run: weight and volume, utilisation, cost and distance.",
        Columns = Cols(
            C("run", "Planning run", filter: true), C("trip", "Trip"), C("vehicle", "Vehicle", filter: true), C("vehicleType", "Vehicle type", filter: true), C("transporter", "Transporter", filter: true),
            C("lane", "Lane", filter: true), C("orders", "Orders", FieldType.Whole), C("weightKg", "Weight (kg)", FieldType.Number), C("volumeCbm", "Volume (CBM)", FieldType.Number),
            C("weightUtilisation", "Weight utilisation", FieldType.Percent), C("volumeUtilisation", "Volume utilisation", FieldType.Percent), C("estimatedCost", "Estimated cost", FieldType.Currency),
            C("distanceKm", "Distance (km)", FieldType.Number), C("shipment", "Shipment"), C("planningDate", "Planning date", FieldType.Date)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Load, FilterNames.Transporter, FilterNames.VehicleType, FilterNames.Lane, FilterNames.Region, FilterNames.ServiceType, FilterNames.Vehicle),
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment"))), new("run", "R02_LOAD_PLANNING_SUMMARY", "Back to the run", Map((FilterNames.Load, "run")))],
        Sorts = Sort("planningDate", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var vehicles = await ctx.Facts.Vehicles();
            foreach (var v in vehicles)
            {
                d.Rows.Add(new ReportRow
                {
                    ["run"] = v.RunRef, ["trip"] = v.TripRef, ["vehicle"] = v.VehicleRef, ["vehicleType"] = v.VehicleType, ["transporter"] = v.TransporterName, ["lane"] = v.Lane, ["orders"] = v.Orders,
                    ["weightKg"] = v.WeightKg, ["volumeCbm"] = v.VolumeCbm, ["weightUtilisation"] = v.WeightUtilisationPct, ["volumeUtilisation"] = v.VolumeUtilisationPct,
                    ["estimatedCost"] = v.EstimatedCost, ["distanceKm"] = v.DistanceKm, ["shipment"] = v.ShipmentRef, ["planningDate"] = v.PlanningDate,
                });
            }

            d.Totals.Add(new("trips", "Vehicles", vehicles.Count));
            d.Totals.Add(new("orders", "Orders", vehicles.Sum(v => v.Orders)));
            d.Totals.Add(new("cost", "Estimated cost", vehicles.Sum(v => v.EstimatedCost), "currency"));
            d.Totals.Add(new("distance", "Distance (km)", vehicles.Sum(v => v.DistanceKm), "number"));
            return d;
        },
    };

    private static ReportSpec VehicleUtilisation() => new()
    {
        Code = "R04_VEHICLE_UTILISATION", Name = "Vehicle utilisation", Category = CatPlanning, Type = ReportType.Analytical, DataSource = "Planning", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Planning, SortOrder = 40, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "How full the vehicles are (weight and volume) and how much of the distance they run loaded. Group by vehicle type, transporter, lane, region or month.",
        Columns = Cols(
            C("vehicleType", "Vehicle type", filter: true), C("transporter", "Transporter", filter: true), C("lane", "Lane", filter: true), C("region", "Region", filter: true), C("month", "Month"), C("date", "Date", FieldType.Date),
            C("vehicle", "Vehicle"), C("trips", "Trips", FieldType.Whole), C("weightKg", "Weight carried (kg)", FieldType.Number, visible: false), C("capacityKg", "Payload (kg)", FieldType.Number, visible: false),
            C("volumeCbm", "Volume carried (CBM)", FieldType.Number, visible: false), C("capacityCbm", "Capacity (CBM)", FieldType.Number, visible: false),
            C("weightUtilisation", "Weight utilisation %", FieldType.Percent), C("volumeUtilisation", "Volume utilisation %", FieldType.Percent),
            C("loadedKm", "Loaded km", FieldType.Number), C("emptyKm", "Empty km", FieldType.Number), C("totalKm", "Total km", FieldType.Number, visible: false), C("loadedKmPct", "Loaded km %", FieldType.Percent)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.VehicleType, FilterNames.Transporter, FilterNames.Lane, FilterNames.Region, FilterNames.ServiceType),
        Groupings = Groups(("vehicleType", "Vehicle type"), ("transporter", "Transporter"), ("lane", "Lane"), ("region", "Region"), ("month", "Month"), ("date", "Date"), ("vehicle", "Vehicle")),
        DefaultGroupBy = ["vehicleType"],
        Measures =
        [
            new("trips", MeasureKind.Count), new("weightUtilisation", MeasureKind.Ratio, "weightKg", "capacityKg", 100m), new("volumeUtilisation", MeasureKind.Ratio, "volumeCbm", "capacityCbm", 100m),
            new("loadedKm", MeasureKind.Sum), new("emptyKm", MeasureKind.Sum), new("weightKg", MeasureKind.Sum), new("capacityKg", MeasureKind.Sum), new("volumeCbm", MeasureKind.Sum), new("capacityCbm", MeasureKind.Sum),
            new("totalKm", MeasureKind.Sum), new("loadedKmPct", MeasureKind.Ratio, "loadedKm", "totalKm", 100m),
        ],
        Drills = [new("vehicleType", "R03_VEHICLE_ALLOCATION", "Vehicles of this type", Map((FilterNames.VehicleType, "vehicleType"))), new("transporter", "R09_TRANSPORTER_SCORECARD", "Transporter scorecard", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("weightUtilisation", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var vehicles = await ctx.Facts.Vehicles();
            foreach (var v in vehicles)
            {
                var loaded = v.LoadedKm;
                var empty = v.EmptyKm;
                d.Rows.Add(new ReportRow
                {
                    ["vehicleType"] = v.VehicleType, ["transporter"] = v.TransporterName, ["lane"] = v.Lane, ["region"] = v.Region, ["month"] = Bucket(v.PlanningDate, "month"), ["date"] = v.PlanningDate, ["vehicle"] = v.VehicleRef,
                    ["trips"] = 1, ["weightKg"] = v.CapacityKg is > 0 ? v.WeightKg : null, ["capacityKg"] = v.CapacityKg is > 0 ? (decimal?)v.CapacityKg : null,
                    ["volumeCbm"] = v.CapacityCbm is > 0 ? v.VolumeCbm : null, ["capacityCbm"] = v.CapacityCbm is > 0 ? v.CapacityCbm : null,
                    ["weightUtilisation"] = v.WeightUtilisationPct, ["volumeUtilisation"] = v.VolumeUtilisationPct, ["loadedKm"] = loaded, ["emptyKm"] = empty,
                    ["totalKm"] = loaded is not null && empty is not null ? loaded + empty : null, ["loadedKmPct"] = loaded is not null && empty is not null ? Pct(loaded.Value, loaded.Value + empty.Value) : null,
                });
            }

            d.Cards.Add(await ctx.CardAsync("WEIGHT_UTIL"));
            d.Cards.Add(await ctx.CardAsync("VOLUME_UTIL"));
            var byType = vehicles.Where(v => v.CapacityKg is > 0).GroupBy(v => v.VehicleType).OrderBy(g => g.Key)
                .Select(g => new ReportRow { ["vehicleType"] = g.Key, ["weight"] = Pct(g.Sum(v => v.WeightKg), g.Sum(v => (decimal)v.CapacityKg!.Value)), ["trips"] = g.Count() }).ToList();
            d.Charts.Add(Chart("util-by-type", "Weight utilisation by vehicle type", "bar", "vehicleType", byType, new ChartSeries("weight", "Weight utilisation %")));
            var knownKm = vehicles.Where(v => v.LoadedKm is not null && v.EmptyKm is not null).ToList();
            d.Totals.Add(new("trips", "Trips", vehicles.Count));
            d.Totals.Add(new("loaded", "Loaded km", knownKm.Sum(v => v.LoadedKm!.Value), "number"));
            d.Totals.Add(new("empty", "Empty km", knownKm.Sum(v => v.EmptyKm!.Value), "number"));
            d.Totals.Add(new("loadedPct", "Loaded km %", Pct(knownKm.Sum(v => v.LoadedKm!.Value), knownKm.Sum(v => v.LoadedKm!.Value + v.EmptyKm!.Value)), "percent"));
            return d;
        },
    };

    private static ReportSpec Unplanned() => new()
    {
        Code = "R05_UNPLANNED", Name = "Unplanned shipment report", Category = CatPlanning, Type = ReportType.Operational, DataSource = "Planning", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Planning, SortOrder = 50,
        Description = "Orders the planner could not place, with the reason and what to do about it.",
        Columns = Cols(
            C("order", "Order", filter: true), C("customer", "Customer", filter: true), C("origin", "Origin", filter: true), C("destination", "Destination", filter: true), C("weightKg", "Weight (kg)", FieldType.Number),
            C("volumeCbm", "Volume (CBM)", FieldType.Number), C("planningDate", "Planning date", FieldType.Date), C("reasonCategory", "Reason category", FieldType.Status, filter: true), C("reason", "Reason"),
            C("suggestedAction", "Suggested action"), C("run", "Planning run"), C("orders", "Orders", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Load, FilterNames.Customer, FilterNames.Lane, FilterNames.Region, FilterNames.Status).Select(f => f.Name == FilterNames.Status ? f.With("unplannedReason") : f).ToList(),
        Groupings = Groups(("reasonCategory", "Reason category"), ("customer", "Customer"), ("origin", "Origin")),
        Measures = [new("orders", MeasureKind.Count), new("weightKg", MeasureKind.Sum), new("volumeCbm", MeasureKind.Sum)],
        Drills = [new("run", "R02_LOAD_PLANNING_SUMMARY", "Planning run", Map((FilterNames.Load, "run")))],
        Sorts = Sort("planningDate", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rows = await ctx.Facts.Unplanned();
            foreach (var u in rows)
            {
                d.Rows.Add(new ReportRow
                {
                    ["order"] = u.OrderRef, ["customer"] = u.Customer, ["origin"] = u.OriginCity, ["destination"] = u.DestinationCity, ["weightKg"] = u.WeightKg, ["volumeCbm"] = u.VolumeCbm,
                    ["planningDate"] = u.PlanningDate, ["reasonCategory"] = u.ReasonCategory, ["reason"] = u.Reason, ["suggestedAction"] = u.SuggestedAction, ["run"] = u.RunRef, ["orders"] = 1,
                });
            }

            d.Totals.Add(new("orders", "Unplanned orders", rows.Count));
            d.Totals.Add(new("weight", "Weight left (kg)", rows.Sum(r => r.WeightKg), "number"));
            d.Charts.Add(Chart("reasons", "Why orders were not planned", "donut", "reasonCategory",
                rows.GroupBy(r => r.ReasonCategory).OrderByDescending(g => g.Count()).Select(g => new ReportRow { ["reasonCategory"] = g.Key, ["orders"] = g.Count() }), new ChartSeries("orders", "Orders")));
            return d;
        },
    };

    private static ReportSpec FtlVsPtl() => new()
    {
        Code = "R06_FTL_VS_PTL", Name = "FTL vs PTL analysis", Category = CatPlanning, Type = ReportType.Analytical, DataSource = "Planning", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Planning, SortOrder = 60, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Full-truck against part-truck loads: cost, transit time and SLA, with the saving from consolidating onto full trucks. Compare by lane, transporter, customer or period.",
        Columns = Cols(
            C("service", "Service", filter: true), C("lane", "Lane", filter: true), C("transporter", "Transporter", filter: true), C("customer", "Customer", filter: true), C("month", "Month"),
            C("shipments", "Shipments", FieldType.Whole), C("ftl", "FTL shipments", FieldType.Whole), C("ptl", "PTL shipments", FieldType.Whole), C("consolidatedFtl", "Consolidated FTL", FieldType.Whole),
            C("ftlCost", "FTL cost", FieldType.Currency), C("ptlCost", "PTL cost", FieldType.Currency), C("savings", "Estimated savings", FieldType.Currency), C("transitHours", "Transit time (h)", FieldType.Number),
            C("slaMet", "SLA met", FieldType.Whole, visible: false), C("slaJudged", "SLA judged", FieldType.Whole, visible: false), C("slaCompliance", "SLA compliance %", FieldType.Percent), C("shipment", "Shipment")),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Lane, FilterNames.Transporter, FilterNames.Customer, FilterNames.ServiceType, FilterNames.Region),
        Groupings = Groups(("service", "Service"), ("lane", "Lane"), ("transporter", "Transporter"), ("customer", "Customer"), ("month", "Month")),
        DefaultGroupBy = ["service"],
        Measures =
        [
            new("shipments", MeasureKind.Count), new("ftl", MeasureKind.Sum), new("ptl", MeasureKind.Sum), new("consolidatedFtl", MeasureKind.Sum), new("ftlCost", MeasureKind.Sum), new("ptlCost", MeasureKind.Sum),
            new("savings", MeasureKind.Sum), new("transitHours", MeasureKind.Avg), new("slaMet", MeasureKind.Sum), new("slaJudged", MeasureKind.Sum), new("slaCompliance", MeasureKind.Ratio, "slaMet", "slaJudged", 100m),
        ],
        Drills = [new("lane", "R37_LANE_PERFORMANCE", "Lane performance", Map((FilterNames.Lane, "lane"))), new("transporter", "R09_TRANSPORTER_SCORECARD", "Transporter scorecard", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("shipments", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var shipments = (await ctx.Facts.Shipments()).Where(s => !s.IsCancelled).ToList();
            var vehicles = (await ctx.Facts.Vehicles()).Where(v => v.ShipmentRef is not null && v.Consolidated && v.CostIfSeparate is not null).ToDictionary(v => v.ShipmentRef!, v => v.CostIfSeparate!.Value - v.EstimatedCost, StringComparer.Ordinal);
            foreach (var s in shipments)
            {
                var ftl = s.Service.Equals("FTL", StringComparison.OrdinalIgnoreCase);
                var ptl = s.Service.Equals("PTL", StringComparison.OrdinalIgnoreCase);
                var judged = s.DeliveredAt is not null && s.DeliverBy is not null;
                d.Rows.Add(new ReportRow
                {
                    ["service"] = s.Service, ["lane"] = s.Lane, ["transporter"] = s.TransporterName, ["customer"] = s.Customer, ["month"] = Bucket(s.PlannedPickupDate, "month"), ["shipments"] = 1,
                    ["ftl"] = One(ftl), ["ptl"] = One(ptl), ["consolidatedFtl"] = One(ftl && s.ConsolidatedFrom > 1), ["ftlCost"] = ftl ? s.FreightEstimate : null, ["ptlCost"] = ptl ? s.FreightEstimate : null,
                    ["savings"] = vehicles.TryGetValue(s.ShipmentRef, out var sv) ? sv : null,
                    ["transitHours"] = s.DispatchedAt is { } o && s.DeliveredAt is { } e ? Math.Round((decimal)(e - o).TotalHours, 1) : null,
                    ["slaMet"] = judged && Day(s.DeliveredAt!.Value) <= s.DeliverBy!.Value ? 1 : 0, ["slaJudged"] = One(judged), ["slaCompliance"] = judged ? (Day(s.DeliveredAt!.Value) <= s.DeliverBy!.Value ? 100m : 0m) : null,
                    ["shipment"] = s.ShipmentRef,
                });
            }

            d.Cards.Add(await ctx.CardAsync("FTL_PCT"));
            d.Cards.Add(await ctx.CardAsync("PTL_PCT"));
            d.Cards.Add(await ctx.CardAsync("DEDICATED_PCT"));
            d.Cards.Add(await ctx.CardAsync("CONSOLIDATION_SAVINGS"));
            d.Charts.Add(Chart("cost-by-service", "Average freight per shipment by service", "bar", "service",
                shipments.Where(s => s.FreightEstimate is not null).GroupBy(s => s.Service).Select(g => new ReportRow { ["service"] = g.Key, ["avg"] = Math.Round(g.Average(s => s.FreightEstimate!.Value), 0) }), new ChartSeries("avg", "Average freight")));
            return d;
        },
    };

    private static ReportSpec Consolidation() => new()
    {
        Code = "R07_CONSOLIDATION_SAVINGS", Name = "Consolidation & savings", Category = CatPlanning, Type = ReportType.Analytical, DataSource = "Planning", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Planning, SortOrder = 70, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "What putting orders on shared trips saved: cost before and after, the extra distance and stops it took.",
        Columns = Cols(
            C("trip", "Trip", filter: true), C("shipment", "Shipment"), C("lane", "Lane", filter: true), C("transporter", "Transporter", filter: true), C("month", "Month"), C("run", "Planning run"),
            C("ordersConsolidated", "Orders consolidated", FieldType.Whole), C("separateTrips", "Separate trips", FieldType.Whole), C("consolidatedTrips", "Consolidated trips", FieldType.Whole),
            C("costBefore", "Cost before", FieldType.Currency), C("costAfter", "Cost after", FieldType.Currency), C("savings", "Savings", FieldType.Currency), C("savingsPct", "Savings %", FieldType.Percent),
            C("additionalKm", "Additional distance (km)", FieldType.Number), C("additionalStops", "Additional stops", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Lane, FilterNames.Transporter, FilterNames.Region, FilterNames.Load),
        Groupings = Groups(("lane", "Lane"), ("transporter", "Transporter"), ("month", "Month")),
        Measures =
        [
            new("ordersConsolidated", MeasureKind.Sum), new("separateTrips", MeasureKind.Sum), new("consolidatedTrips", MeasureKind.Sum), new("costBefore", MeasureKind.Sum), new("costAfter", MeasureKind.Sum),
            new("savings", MeasureKind.Sum), new("savingsPct", MeasureKind.Ratio, "savings", "costBefore", 100m), new("additionalKm", MeasureKind.Sum), new("additionalStops", MeasureKind.Sum),
        ],
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment"))), new("run", "R03_VEHICLE_ALLOCATION", "Vehicles of the run", Map((FilterNames.Load, "run")))],
        Sorts = Sort("savings", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var trips = (await ctx.Facts.Vehicles()).Where(v => v.Consolidated && v.CostIfSeparate is not null).ToList();
            foreach (var v in trips)
            {
                var saving = v.CostIfSeparate!.Value - v.EstimatedCost;
                d.Rows.Add(new ReportRow
                {
                    ["trip"] = v.TripRef, ["shipment"] = v.ShipmentRef, ["lane"] = v.Lane, ["transporter"] = v.TransporterName, ["month"] = Bucket(v.PlanningDate, "month"), ["run"] = v.RunRef,
                    ["ordersConsolidated"] = v.Orders, ["separateTrips"] = v.Orders, ["consolidatedTrips"] = 1, ["costBefore"] = v.CostIfSeparate, ["costAfter"] = v.EstimatedCost, ["savings"] = saving,
                    ["savingsPct"] = Pct(saving, v.CostIfSeparate.Value), ["additionalKm"] = v.ExtraKmFromConsolidation, ["additionalStops"] = v.ExtraStopsFromConsolidation,
                });
            }

            d.Cards.Add(await ctx.CardAsync("CONSOLIDATION_SAVINGS"));
            d.Totals.Add(new("trips", "Consolidated trips", trips.Count));
            d.Totals.Add(new("orders", "Orders consolidated", trips.Sum(t => t.Orders)));
            d.Totals.Add(new("before", "Cost before", trips.Sum(t => t.CostIfSeparate!.Value), "currency"));
            d.Totals.Add(new("after", "Cost after", trips.Sum(t => t.EstimatedCost), "currency"));
            d.Charts.Add(Chart("savings-month", "Consolidation savings by month", "bar", "month",
                trips.GroupBy(t => Bucket(t.PlanningDate, "month")).OrderBy(g => g.Key).Select(g => new ReportRow { ["month"] = g.Key, ["savings"] = g.Sum(t => t.CostIfSeparate!.Value - t.EstimatedCost) }), new ChartSeries("savings", "Savings")));
            return d;
        },
    };

    private static ReportSpec PlannedVsActualTrip() => new()
    {
        Code = "R08_PLANNED_VS_ACTUAL_TRIP", Name = "Planned vs actual trip", Category = CatPlanning, Type = ReportType.Operational, DataSource = "Planning + Tracking + Freight Contracts", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Planning, SortOrder = 80, ExportFormats = "csv,xlsx,pdf",
        Description = "For each shipment, what was planned (vehicle, distance, duration, cost, stops) against what happened, with the variance.",
        Columns = Cols(
            C("shipment", "Shipment", filter: true), C("lane", "Lane", filter: true), C("transporter", "Transporter", filter: true), C("plannedVehicle", "Planned vehicle"), C("actualVehicle", "Actual vehicle"),
            C("plannedDistance", "Planned km", FieldType.Number), C("actualDistance", "Actual km", FieldType.Number), C("distanceVariance", "Distance variance (km)", FieldType.Number),
            C("plannedDuration", "Planned duration (min)", FieldType.Whole), C("actualDuration", "Actual duration (min)", FieldType.Whole), C("durationVariance", "Duration variance (min)", FieldType.Whole),
            C("plannedCost", "Planned cost", FieldType.Currency), C("actualCost", "Actual cost", FieldType.Currency), C("costVariance", "Cost variance", FieldType.Currency),
            C("plannedStops", "Planned stops", FieldType.Whole), C("actualStops", "Actual stops", FieldType.Whole), C("stopsVariance", "Stops variance", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Shipment, FilterNames.Transporter, FilterNames.Lane, FilterNames.Region, FilterNames.VehicleType),
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("costVariance", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var shipments = (await ctx.Facts.Shipments()).Where(s => !s.IsCancelled).ToList();
            var plan = (await ctx.Facts.Vehicles()).Where(v => v.ShipmentRef is not null).GroupBy(v => v.ShipmentRef!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var tracks = (await ctx.Facts.Trips()).Where(t => t.ShipmentRef is not null).GroupBy(t => t.ShipmentRef!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var s in shipments)
            {
                plan.TryGetValue(s.ShipmentRef, out var p);
                tracks.TryGetValue(s.ShipmentRef, out var t);
                if (p is null && t is null)
                {
                    continue;
                }

                var plannedKm = t?.PlannedDistanceKm ?? p?.DistanceKm ?? s.DistanceKm;
                var plannedCost = p?.EstimatedCost ?? s.PlannedCost;
                d.Rows.Add(new ReportRow
                {
                    ["shipment"] = s.ShipmentRef, ["lane"] = s.Lane, ["transporter"] = s.TransporterName, ["plannedVehicle"] = p?.VehicleType ?? s.VehicleType, ["actualVehicle"] = s.VehicleRef is null ? null : $"{s.VehicleRef} ({s.VehicleType})",
                    ["plannedDistance"] = plannedKm, ["actualDistance"] = t?.ActualDistanceKm, ["distanceVariance"] = plannedKm is { } pk && t?.ActualDistanceKm is { } ak ? ak - pk : null,
                    ["plannedDuration"] = t?.PlannedDurationMin, ["actualDuration"] = t?.ActualDurationMin, ["durationVariance"] = t is { PlannedDurationMin: { } pm, ActualDurationMin: { } am } ? am - pm : null,
                    ["plannedCost"] = plannedCost, ["actualCost"] = s.FreightEstimate, ["costVariance"] = plannedCost is { } pc && s.FreightEstimate is { } ac ? ac - pc : null,
                    ["plannedStops"] = t?.PlannedStops ?? p?.Stops ?? s.Stops, ["actualStops"] = t?.ActualStops, ["stopsVariance"] = t is { ActualStops: { } xs } ? xs - (t.PlannedStops ?? p?.Stops ?? s.Stops) : null,
                });
            }

            d.Notes.Add("Actual distance, duration and stops come from Tracking; actual cost is the contract freight kept against the shipment. A blank means that side is not available yet.");
            d.Totals.Add(new("shipments", "Shipments compared", d.Rows.Count));
            var withCost = d.Rows.Where(r => r["costVariance"] is decimal).ToList();
            d.Totals.Add(new("cost", "Net cost variance", withCost.Sum(r => (decimal)r["costVariance"]!), "currency"));
            return d;
        },
    };
}
