using Tms.SharedKernel.Contracts;
using static Tms.Modules.Reports.Domain.Kit;

namespace Tms.Modules.Reports.Domain;

/// <summary>R36–R39 and the executive dashboard: reports that read across modules by shipment, lane and transporter reference.</summary>
internal static class CrossReports
{
    public static IEnumerable<ReportSpec> All()
    {
        yield return Executive();
        yield return Shipment360();
        yield return LanePerformance();
        yield return CostVsPerformance();
        yield return CostVsService();
    }

    // ---- shared by R01, R38 and R39

    internal sealed record Carrier(string Name, int Shipments, decimal? Spend, decimal? CostPerShipment, decimal? Otd, decimal? Otp, decimal? Score, decimal? Claims, decimal? Placement, decimal? Performance, string Quadrant);

    internal static async Task<(List<Carrier> Carriers, decimal? CostLine, decimal? PerfLine)> Carriers(ReportContext ctx)
    {
        var shipments = (await ctx.Facts.Shipments()).Where(s => !s.IsCancelled && s.TransporterName is not null).ToList();
        var names = shipments.Select(s => s.TransporterName!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var k = await KpisBy(ctx.Facts.Without(FilterNames.Transporter), FilterNames.Transporter, names, ["FREIGHT_SPEND", "COST_PER_SHIPMENT", "OTD", "OTP", "TRANSPORTER_SCORE", "CLAIMS_RATE", "PLACEMENT_COMPLIANCE"]);
        var raw = names.Select(n => new
        {
            Name = n, Count = shipments.Count(s => s.TransporterName!.Equals(n, StringComparison.OrdinalIgnoreCase)), Spend = k[n]["FREIGHT_SPEND"].Value, Cost = k[n]["COST_PER_SHIPMENT"].Value, Otd = k[n]["OTD"].Value,
            Otp = k[n]["OTP"].Value, Score = k[n]["TRANSPORTER_SCORE"].Value, Claims = k[n]["CLAIMS_RATE"].Value, Placement = k[n]["PLACEMENT_COMPLIANCE"].Value,
        }).Select(x => (x, Perf: x.Score ?? x.Otd)).ToList();
        var classified = raw.Where(r => r.x.Cost is not null && r.Perf is not null).ToList();
        var costLine = Middle(classified.Select(r => r.x.Cost!.Value), ctx.Settings);
        var perfLine = Middle(classified.Select(r => r.Perf!.Value), ctx.Settings);
        var list = raw.Select(r =>
        {
            var q = r.x.Cost is null || r.Perf is null || costLine is null || perfLine is null ? "Not classified"
                : $"{(r.x.Cost <= costLine ? "Low cost" : "High cost")} / {(r.Perf >= perfLine ? "High performance" : "Low performance")}";
            return new Carrier(r.x.Name, r.x.Count, r.x.Spend, r.x.Cost, r.x.Otd, r.x.Otp, r.x.Score, r.x.Claims, r.x.Placement, r.Perf, q);
        }).OrderByDescending(c => c.Shipments).ToList();
        return (list, costLine, perfLine);
    }

    internal static ChartData Scatter(ReportContext ctx, List<Carrier> carriers, decimal? costLine, decimal? perfLine)
    {
        var lines = new List<ChartLine>();
        if (costLine is { } c)
        {
            lines.Add(new ChartLine("x", c, "Cost line"));
        }

        if (perfLine is { } p)
        {
            lines.Add(new ChartLine("y", p, "Performance line"));
        }

        return Scatter(ctx, carriers, lines);
    }

    private static readonly string[] Quadrants = ["Low cost / High performance", "Low cost / Low performance", "High cost / High performance", "High cost / Low performance"];

    private static ChartData Scatter(ReportContext ctx, List<Carrier> carriers, List<ChartLine> lines) => new(
        "cost-vs-performance", "Cost per shipment against performance", "scatter", "cost",
        Quadrants.Select(q => new ChartSeries(q, q, q switch
        {
            "Low cost / High performance" => "#16a34a", "Low cost / Low performance" => "#eab308", "High cost / High performance" => "#2563eb", _ => "#dc2626",
        })).ToList(),
        carriers.Where(c => c.CostPerShipment is not null && c.Performance is not null).Select(c => new ReportRow { ["cost"] = c.CostPerShipment, ["performance"] = c.Performance, ["label"] = c.Name, ["quadrant"] = c.Quadrant, ["shipments"] = c.Shipments }).ToList(),
        $"Dividing lines are the {ctx.Settings.ClassificationMethod.ToLowerInvariant()} of the carriers shown. Performance is the Transporter Management score, or OTD where a carrier has no score.",
        "Performance", "Cost per shipment (₹)", null, null, lines);

    // ---- R01

    private static ReportSpec Executive() => new()
    {
        Code = "R01_EXECUTIVE_DASHBOARD", Name = "Executive TMS dashboard", Category = CatExecutive, Type = ReportType.Dashboard, DataSource = "All modules", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Executive, SortOrder = 10, ExportFormats = "xlsx,pdf", Comparison = true, TrendBuckets = 6,
        Description = "The state of the transport operation in one page: shipments and spend, service, utilisation, proof, tracking, savings and critical exceptions, each against the previous period and the same period last year, with a path into the detail.",
        Columns = Cols(
            C("transporter", "Transporter", filter: true), C("shipments", "Shipments", FieldType.Whole), C("spend", "Freight spend", FieldType.Currency), C("costPerShipment", "Cost / shipment", FieldType.Currency), C("otd", "OTD %", FieldType.Percent),
            C("otp", "OTP %", FieldType.Percent), C("score", "Score", FieldType.Number), C("claims", "Claims %", FieldType.Percent), C("quadrant", "Cost / performance")),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Period, FilterNames.Compare, FilterNames.Region, FilterNames.Customer, FilterNames.ServiceType, FilterNames.BusinessUnit),
        Drills = [new("transporter", "R09_TRANSPORTER_SCORECARD", "Transporter scorecard", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("shipments", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData { Fixed = true };
            foreach (var code in new[]
            {
                "SHIPMENTS", "FREIGHT_SPEND", "COST_PER_SHIPMENT", "COST_PER_TON", "COST_PER_TON_KM", "FTL_PCT", "PTL_PCT", "DEDICATED_PCT", "OTP", "OTD", "TRANSPORTER_SCORE", "TENDER_ACCEPTANCE",
                "WEIGHT_UTIL", "VOLUME_UTIL", "POD_COMPLIANCE", "SHORTAGE_PCT", "DAMAGE_PCT", "TRACKING_COVERAGE", "TRACKING_LOST", "ROUTE_DEVIATIONS", "CONSOLIDATION_SAVINGS", "PLANNING_SAVINGS", "OPEN_CRITICAL_EXCEPTIONS",
            })
            {
                d.Cards.Add(await ctx.CardAsync(code));
            }

            d.Charts.Add(Chart("freight-trend", "Freight spend by month", "line", "bucket", await ctx.TrendAsync(["FREIGHT_SPEND"], 6), new ChartSeries("FREIGHT_SPEND", "Freight spend")) with { DrillReport = "R35_FREIGHT_RATING_AUDIT" });
            d.Charts.Add(Chart("service-trend", "OTD and OTP by month", "line", "bucket", await ctx.TrendAsync(["OTD", "OTP"], 6), new ChartSeries("OTD", "On-time delivery %"), new ChartSeries("OTP", "On-time pickup %")) with { DrillReport = "R14_OTP_OTD" });
            var (carriers, costLine, perfLine) = await Carriers(ctx);
            d.Charts.Add(Chart("transporters", "Transporter on-time delivery", "bar", "transporter",
                carriers.Where(c => c.Otd is not null).OrderByDescending(c => c.Otd).Take(ctx.Settings.TopN).Select(c => new ReportRow { ["transporter"] = c.Name, ["otd"] = c.Otd }), new ChartSeries("otd", "OTD %")) with { DrillReport = "R10_TRANSPORTER_RANKING" });
            var lanes = (await ctx.Facts.Shipments()).Where(s => !s.IsCancelled && s.FreightEstimate is not null).GroupBy(s => s.Lane).OrderByDescending(g => g.Sum(s => s.FreightEstimate!.Value)).Take(ctx.Settings.TopN)
                .Select(g => new ReportRow { ["lane"] = g.Key, ["spend"] = g.Sum(s => s.FreightEstimate!.Value) }).ToList();
            d.Charts.Add(Chart("lanes", "Freight spend by lane", "bar", "lane", lanes, new ChartSeries("spend", "Freight spend")) with { DrillReport = "R37_LANE_PERFORMANCE" });
            d.Charts.Add(Scatter(ctx, carriers, costLine, perfLine) with { DrillReport = "R38_COST_VS_PERFORMANCE" });
            var open = (await ctx.Facts.Exceptions()).Where(e => e.Status != "Resolved").ToList();
            d.Charts.Add(Chart("exceptions", "Open exceptions by module and severity", "stackedBar", "module",
                open.GroupBy(e => e.Module).Select(g => new ReportRow { ["module"] = g.Key, ["Critical"] = g.Count(e => e.Severity == "Critical"), ["High"] = g.Count(e => e.Severity == "High"), ["Warning"] = g.Count(e => e.Severity == "Warning"), ["Info"] = g.Count(e => e.Severity == "Info") }),
                new ChartSeries("Critical", "Critical", "#dc2626"), new ChartSeries("High", "High", "#f97316"), new ChartSeries("Warning", "Warning", "#eab308"), new ChartSeries("Info", "Info", "#3b82f6")) with { DrillReport = "R16_TRANSPORTER_EXCEPTIONS" });
            foreach (var c in carriers)
            {
                d.Rows.Add(new ReportRow { ["transporter"] = c.Name, ["shipments"] = c.Shipments, ["spend"] = c.Spend, ["costPerShipment"] = c.CostPerShipment, ["otd"] = c.Otd, ["otp"] = c.Otp, ["score"] = c.Score, ["claims"] = c.Claims, ["quadrant"] = c.Quadrant });
            }

            return d;
        },
    };

    // ---- R36

    private static ReportSpec Shipment360() => new()
    {
        Code = "R36_SHIPMENT_360", Name = "Shipment 360", Category = CatCross, Type = ReportType.Drilldown, DataSource = "All modules", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Cross, SortOrder = 360, ExportFormats = "xlsx,pdf",
        Description = "One shipment across every module: order, planning, vehicle, transporter, contract, tracking, delivery, proof, discrepancies, exceptions and freight, with a timeline. Open a shipment from any report, or search here.",
        Columns = Cols(
            C("shipment", "Shipment", filter: true), C("status", "Status", FieldType.Status, filter: true), C("customer", "Customer", filter: true), C("lane", "Lane", filter: true), C("transporter", "Transporter", filter: true), C("vehicle", "Vehicle"),
            C("service", "Service", filter: true), C("weightKg", "Weight (kg)", FieldType.Number), C("freight", "Contract freight", FieldType.Currency), C("deliveryStatus", "Delivery", FieldType.Status), C("podStatus", "POD", FieldType.Status),
            C("risk", "Delivery risk", FieldType.Status), C("openExceptions", "Open exceptions", FieldType.Whole), C("pickupDate", "Pickup date", FieldType.Date)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Shipment, FilterNames.Search, FilterNames.Customer, FilterNames.Transporter, FilterNames.Lane, FilterNames.ServiceType, FilterNames.Status),
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("pickupDate", true), WideOnFilter = FilterNames.Shipment,
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var shipments = (await ctx.Facts.Shipments()).ToList();
            var finance = ctx.Principal.Has(ReportingPermissions.Contracts);
            if (ctx.Filters.Many(FilterNames.Shipment) is [var wanted])
            {
                var s = shipments.FirstOrDefault(x => x.ShipmentRef.Equals(wanted, StringComparison.OrdinalIgnoreCase));
                if (s is null)
                {
                    d.Notes.Add($"No shipment {wanted} was found, or you may not see it.");
                    return d;
                }

                await BuildDetail(ctx, d, s, finance);
                d.Fixed = true;
                return d;
            }

            var deliveries = (await ctx.Facts.Deliveries()).Where(x => x.ShipmentRef is not null).GroupBy(x => x.ShipmentRef!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var pods = (await ctx.Facts.Pods()).Where(x => x.ShipmentRef is not null).GroupBy(x => x.ShipmentRef!).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var trips = (await ctx.Facts.Trips()).Where(x => x.ShipmentRef is not null).GroupBy(x => x.ShipmentRef!).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (var s in shipments.Where(s => ctx.Filters.MatchesSearch([s.ShipmentRef, s.Customer, s.TransporterName, s.Lane, s.VehicleRef])))
            {
                deliveries.TryGetValue(s.ShipmentRef, out var dv);
                pods.TryGetValue(s.ShipmentRef, out var pd);
                trips.TryGetValue(s.ShipmentRef, out var tk);
                d.Rows.Add(new ReportRow
                {
                    ["shipment"] = s.ShipmentRef, ["status"] = s.Status, ["customer"] = s.Customer, ["lane"] = s.Lane, ["transporter"] = s.TransporterName, ["vehicle"] = s.VehicleRef, ["service"] = s.Service, ["weightKg"] = s.WeightKg,
                    ["freight"] = finance ? s.FreightEstimate : null, ["deliveryStatus"] = dv is null ? null : dv.All(x => x.Status is "Delivered") ? "Delivered" : dv.Any(x => x.Status is "Failed" or "Refused") ? "Exceptions" : dv.Any(x => x.Status == "PartiallyDelivered") ? "PartiallyDelivered" : "In progress",
                    ["podStatus"] = pd is null ? null : pd.All(x => x.Status == "Accepted") ? "Accepted" : pd.Any(x => x.Status == "Rejected") ? "Rejected" : "Pending", ["risk"] = tk?.Risk, ["openExceptions"] = tk?.OpenExceptions, ["pickupDate"] = s.PlannedPickupDate,
                });
            }

            d.Notes.Add("Open a shipment to see it across every module." + (finance ? string.Empty : " Freight figures are shown only to people who may see commercial data."));
            return d;
        },
    };

    private static async Task BuildDetail(ReportContext ctx, ReportData d, ShipmentReportFact s, bool finance)
    {
        var f = ctx.Facts;
        var plan = (await f.Vehicles()).FirstOrDefault(v => v.ShipmentRef == s.ShipmentRef);
        var tender = (await f.Tenders()).Where(t => t.ShipmentRef == s.ShipmentRef).OrderBy(t => t.OfferedAt).ToList();
        var placements = (await f.Placements()).Where(p => p.ShipmentRef == s.ShipmentRef).ToList();
        var deliveries = (await f.Deliveries()).Where(x => x.ShipmentRef == s.ShipmentRef).ToList();
        var pods = (await f.Pods()).Where(x => x.ShipmentRef == s.ShipmentRef).ToList();
        var discrepancies = (await f.Discrepancies()).Where(x => x.ShipmentRef == s.ShipmentRef).ToList();
        var trip = (await f.Trips()).FirstOrDefault(t => t.ShipmentRef == s.ShipmentRef);
        var deviations = (await f.Deviations()).Where(x => x.ShipmentRef == s.ShipmentRef).ToList();
        var gaps = (await f.Gaps()).Where(x => x.ShipmentRef == s.ShipmentRef).ToList();
        var exceptions = (await f.Exceptions()).Where(x => x.ShipmentRef == s.ShipmentRef).ToList();
        var score = s.TransporterId is { } tid ? (await f.Scorecards()).Where(x => x.TransporterId == tid).OrderByDescending(x => x.PeriodEnd).FirstOrDefault() : null;
        var rating = finance ? (await f.Ratings()).Where(x => x.ShipmentRef == s.ShipmentRef).OrderByDescending(x => x.RatedAt).FirstOrDefault() : null;
        IReadOnlyDictionary<string, string> Shipment(string name = FilterNames.Shipment) => new Dictionary<string, string> { [name] = s.ShipmentRef };
        IReadOnlyDictionary<string, string> Transporter() => new Dictionary<string, string> { [FilterNames.Transporter] = s.TransporterName ?? string.Empty };

        d.Sections.Add(new("order", "Order", "Shipments",
        [
            new("Shipment", s.ShipmentRef), new("Status", s.Status), new("Customer", s.Customer), new("Lane", s.Lane), new("Orders on this shipment", s.Orders), new("Stops", s.Stops), new("Weight (kg)", s.WeightKg, "number"),
            new("Volume (CBM)", s.VolumeCbm, "number"), new("Distance (km)", s.DistanceKm, "number"), new("Planned pickup date", s.PlannedPickupDate, "date"), new("Deliver by", s.DeliverBy, "date"),
        ]));
        d.Sections.Add(new("planning", "Planning", "Planning",
        [
            new("Service", s.Service), new("Plan", s.PlanRef ?? plan?.RunRef, null, "R03_VEHICLE_ALLOCATION", plan is null ? null : new Dictionary<string, string> { [FilterNames.Load] = plan.RunRef }), new("Planned vehicle type", plan?.VehicleType ?? s.VehicleType),
            new("Planned cost", plan?.EstimatedCost ?? s.PlannedCost, "currency"), new("Planned distance (km)", plan?.DistanceKm ?? s.DistanceKm, "number"), new("Orders consolidated", s.ConsolidatedFrom > 1 ? s.ConsolidatedFrom : null),
            new("Weight utilisation %", plan?.WeightUtilisationPct, "percent"), new("Tendered", s.TenderedAt, "datetime", "R12_TENDER_PERFORMANCE", Shipment()), new("Accepted", s.AcceptedAt, "datetime"), new("Dispatched", s.DispatchedAt, "datetime"),
        ], tender.Count == 0 ? null : tender.Select(t => new ReportRow { ["transporter"] = t.TransporterName, ["offered"] = t.OfferedAt, ["responded"] = t.RespondedAt, ["outcome"] = t.Outcome }).ToList(),
        Cols(C("transporter", "Tendered to"), C("offered", "Offered", FieldType.DateTime), C("responded", "Responded", FieldType.DateTime), C("outcome", "Outcome", FieldType.Status))));
        d.Sections.Add(new("vehicle", "Vehicle & driver", "Transporters",
        [
            new("Vehicle", s.VehicleRef), new("Vehicle type", s.VehicleType), new("Payload (kg)", s.VehiclePayloadKg, "number"), new("Driver", s.DriverName),
            .. placements.Take(1).SelectMany(p => new[] { new SectionItem("Placement", p.Outcome, null, "R13_PLACEMENT_COMPLIANCE", Shipment()), new SectionItem("Placement delay (min)", p.DelayMinutes) }),
        ]));
        d.Sections.Add(new("transporter", "Transporter", "Transporters",
        [
            new("Transporter", s.TransporterName, null, "R09_TRANSPORTER_SCORECARD", s.TransporterName is null ? null : Transporter()), new("Overall score", score?.OverallScore, "number"), new("OTD %", score?.Otd, "percent"),
            new("OTP %", score?.Otp, "percent"), new("POD compliance %", score?.PodCompliance, "percent"), new("Score period ends", score?.PeriodEnd, "date"),
        ]));
        if (finance)
        {
            d.Sections.Add(new("contract", "Contract", "Freight Contracts",
            [
                new("Contract", rating?.ContractRef ?? s.ContractRef, null, "R33_RATE_SLAB", (rating?.ContractRef ?? s.ContractRef) is { } c ? new Dictionary<string, string> { [FilterNames.Contract] = c } : null),
                new("Contract version", rating?.ContractVersion), new("Rate", rating is null ? null : $"{rating.RateCode} v{rating.RateVersion}"), new("Selected slab", rating?.SelectedSlab),
            ]));
        }

        d.Sections.Add(new("tracking", "Tracking", "Tracking",
        [
            new("Tracking health", trip?.Health, null, "R28_TRACKING_HEALTH", Shipment()), new("Delivery risk", trip?.Risk, null, "R25_ETA_DELAY", Shipment()), new("Planned ETA", trip?.PlannedEta, "datetime"), new("Latest ETA", trip?.LatestEta, "datetime"),
            new("Actual arrival", trip?.ActualArrival, "datetime"), new("Delay (min)", trip is null ? null : Minutes(trip.ActualArrival ?? trip.LatestEta, trip.PlannedEta), "number"), new("Planned distance (km)", trip?.PlannedDistanceKm, "number"),
            new("Actual distance (km)", trip?.ActualDistanceKm, "number", "R30_PLANNED_VS_ACTUAL_ROUTE", Shipment()), new("Route deviation (km)", trip?.DeviationKm, "number", "R26_ROUTE_DEVIATION", Shipment()),
            new("Dwell (min)", trip?.DwellMinutes, "number", "R27_DWELL_TIME", Shipment()), new("Tracking gaps", gaps.Count, null, "R29_TRACKING_GAPS", Shipment()), new("Last seen", trip?.LastSeenAt, "datetime"),
        ]));
        d.Sections.Add(new("delivery", "Delivery", "Deliveries", [new("Deliveries", deliveries.Count, null, "R17_DELIVERY_PERFORMANCE", Shipment()), new("Late", deliveries.Count(x => x.OnTime == false)), new("Delivered by", s.DeliverBy, "date"), new("Delivered at", s.DeliveredAt, "datetime")],
            deliveries.Select(x => new ReportRow { ["delivery"] = x.DeliveryRef, ["customer"] = x.Customer, ["status"] = x.Status, ["onTime"] = Yn(x.OnTime), ["completedAt"] = x.CompletedAt, ["delayReason"] = x.OnTime == false ? $"{x.DelayReason ?? "No reason recorded"} ({x.DelayResponsibility ?? "not yet judged"})" : null }).ToList(),
            Cols(C("delivery", "Delivery"), C("customer", "Customer"), C("status", "Status", FieldType.Status), C("onTime", "On time"), C("completedAt", "Completed", FieldType.DateTime), C("delayReason", "Delay reason"))));
        d.Sections.Add(new("pod", "Proof of delivery", "Deliveries", [new("Proofs", pods.Count, null, "R19_POD_AGEING", new Dictionary<string, string> { [FilterNames.Shipment] = s.ShipmentRef, ["openOnly"] = "=false" })],
            pods.Select(x => new ReportRow { ["pod"] = x.PodRef, ["status"] = x.Status, ["submittedAt"] = x.SubmittedAt, ["reviewedAt"] = x.ReviewedAt, ["rejection"] = x.RejectionReason }).ToList(),
            Cols(C("pod", "POD"), C("status", "Status", FieldType.Status), C("submittedAt", "Submitted", FieldType.DateTime), C("reviewedAt", "Reviewed", FieldType.DateTime), C("rejection", "Rejection reason"))));
        d.Sections.Add(new("discrepancies", "Shortage & damage", "Deliveries", [new("Lines", discrepancies.Count, null, "R21_SHORTAGE", Shipment())],
            discrepancies.Select(x => new ReportRow { ["type"] = x.Type, ["sku"] = x.Sku, ["quantity"] = x.Quantity, ["reason"] = x.Reason, ["claim"] = x.ClaimRef }).ToList(),
            Cols(C("type", "Type"), C("sku", "SKU"), C("quantity", "Quantity", FieldType.Number), C("reason", "Reason"), C("claim", "Claim"))));
        d.Sections.Add(new("exceptions", "Exceptions", null, [new("Open", exceptions.Count(x => x.Status != "Resolved"), null, "R16_TRANSPORTER_EXCEPTIONS", Shipment())],
            exceptions.Select(x => new ReportRow { ["module"] = x.Module, ["type"] = x.Type, ["severity"] = x.Severity, ["status"] = x.Status, ["createdAt"] = x.CreatedAt, ["owner"] = x.Owner }).ToList(),
            Cols(C("module", "Raised by"), C("type", "Exception"), C("severity", "Severity", FieldType.Status), C("status", "Status", FieldType.Status), C("createdAt", "Created", FieldType.DateTime), C("owner", "Owner"))));
        if (finance)
        {
            d.Sections.Add(new("freight", "Freight", "Freight Contracts",
            [
                new("Contract freight kept", s.FreightEstimate, "currency"), new("Base freight", rating?.BaseFreight, "currency"), new("DPH (diesel)", rating?.Dph, "currency"), new("Accessorials", rating?.Accessorials, "currency"),
                new("Discount", rating?.Discount, "currency"), new("Final freight", rating?.FinalFreight, "currency", "R35_FREIGHT_RATING_AUDIT", Shipment()), new("Planned cost", plan?.EstimatedCost ?? s.PlannedCost, "currency"),
                new("Calculation version", rating?.CalculationVersion),
            ]));
        }

        var timeline = new List<(DateTimeOffset At, string Module, string Event)>();
        void Add(DateTimeOffset? at, string module, string text)
        {
            if (at is { } a)
            {
                timeline.Add((a, module, text));
            }
        }

        Add(s.TenderedAt, "Shipments", $"Tendered{(s.TransporterName is null ? string.Empty : $" to {s.TransporterName}")}");
        Add(s.AcceptedAt, "Shipments", "Accepted with a vehicle and driver");
        Add(s.PlannedPickupAt, "Planning", "Pickup planned for");
        Add(s.ActualPickupAt, "Shipments", "Picked up");
        Add(s.DispatchedAt, "Shipments", "Dispatched");
        Add(trip?.PlannedEta, "Tracking", "ETA at planning");
        Add(trip?.ActualArrival, "Tracking", "Arrived (tracking)");
        foreach (var x in deliveries)
        {
            Add(x.CompletedAt, "Deliveries", $"{x.DeliveryRef}: {x.Status}{(x.OnTime == false ? $" late — {x.DelayReason ?? "no reason recorded"} ({x.DelayResponsibility ?? "not yet judged"})" : string.Empty)}");
        }

        foreach (var x in pods)
        {
            Add(x.SubmittedAt, "Deliveries", $"{x.PodRef} submitted");
            Add(x.RejectedAt, "Deliveries", $"{x.PodRef} rejected: {x.RejectionReason ?? "no reason recorded"}");
            Add(x.ResubmittedAt, "Deliveries", $"{x.PodRef} resubmitted");
            if (x.Status == "Accepted")
            {
                Add(x.ReviewedAt, "Deliveries", $"{x.PodRef} accepted");
            }
        }

        foreach (var x in deviations)
        {
            Add(x.DetectedAt, "Tracking", $"Left the route by {x.DeviationKm:0.#} km");
        }

        foreach (var x in gaps)
        {
            Add(x.GapStart, "Tracking", $"Tracking gap of {x.DurationMin} min");
        }

        foreach (var x in exceptions)
        {
            Add(x.CreatedAt, x.Module, $"Exception: {x.Type} ({x.Severity})");
            Add(x.ResolvedAt, x.Module, $"Exception resolved: {x.Type}");
        }

        Add(rating?.RatedAt, "Freight Contracts", "Freight rated");
        d.Sections.Add(new("timeline", "Timeline", null, [], timeline.OrderBy(t => t.At).Select(t => new ReportRow { ["at"] = t.At, ["module"] = t.Module, ["event"] = t.Event }).ToList(),
            Cols(C("at", "When", FieldType.DateTime), C("module", "Module"), C("event", "Event"))));
        d.Notes.Add("Each section links to the report that holds its detail, keeping this shipment as the filter.");
    }

    // ---- R37

    private static ReportSpec LanePerformance() => new()
    {
        Code = "R37_LANE_PERFORMANCE", Name = "Lane performance", Category = CatCross, Type = ReportType.Dashboard, DataSource = "All modules", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Cross, SortOrder = 370, ExportFormats = "csv,xlsx,pdf", Comparison = false,
        Description = "Each lane against every measure: shipments, spend, cost per shipment and km, OTP, OTD, POD, utilisation, delay, deviation, dwell, the main transporter and what was contracted against what was paid. Rank lanes by any column.",
        Columns = Cols(
            C("rank", "Rank", FieldType.Whole), C("lane", "Lane", filter: true), C("region", "Region", filter: true), C("shipments", "Shipments", FieldType.Whole), C("spend", "Freight spend", FieldType.Currency), C("costPerShipment", "Cost / shipment", FieldType.Currency),
            C("costPerKm", "Cost / km", FieldType.Currency), C("otp", "OTP %", FieldType.Percent), C("otd", "OTD %", FieldType.Percent), C("podCompliance", "POD compliance %", FieldType.Percent), C("weightUtil", "Weight utilisation %", FieldType.Percent),
            C("volumeUtil", "Volume utilisation %", FieldType.Percent), C("avgDelayMin", "Avg delay (min)", FieldType.Number), C("routeDeviationKm", "Route deviation (km)", FieldType.Number), C("avgDwellMin", "Avg dwell (min)", FieldType.Number),
            C("topTransporter", "Top transporter"), C("contractedRate", "Contracted rate (base)", FieldType.Currency), C("actualAverage", "Actual average freight", FieldType.Currency)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, "rankBy", FilterNames.Region, FilterNames.Transporter, FilterNames.Customer, FilterNames.ServiceType, FilterNames.VehicleType),
        Drills = [new("lane", "R10_TRANSPORTER_RANKING", "Transporters on this lane", Map((FilterNames.Lane, "lane"))), new("otd", "R17_DELIVERY_PERFORMANCE", "Late deliveries on this lane", Map((FilterNames.Lane, "lane"), ("onTime", "=No"))),
            new("spend", "R35_FREIGHT_RATING_AUDIT", "Rating audit of this lane", Map((FilterNames.Lane, "lane")))],
        Sorts = Sort("spend", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData { Fixed = true };
            var shipments = (await ctx.Facts.Shipments()).Where(s => !s.IsCancelled).ToList();
            var lanes = shipments.GroupBy(s => s.Lane).OrderByDescending(g => g.Count()).Take(60).Select(g => g.Key).ToList();
            var trips = await ctx.Facts.Trips();
            var ratings = ctx.Principal.Has(ReportingPermissions.Contracts) ? await ctx.Facts.Ratings() : [];
            var deliveries = await ctx.Facts.Deliveries();
            var kpis = await KpisBy(ctx.Facts.Without(FilterNames.Lane), FilterNames.Lane, lanes, ["FREIGHT_SPEND", "COST_PER_SHIPMENT", "OTP", "OTD", "POD_COMPLIANCE", "WEIGHT_UTIL", "VOLUME_UTIL"]);
            var rows = new List<ReportRow>();
            foreach (var lane in lanes)
            {
                var mine = shipments.Where(s => s.Lane == lane).ToList();
                var priced = mine.Where(s => s.FreightEstimate is not null && s.DistanceKm is > 0).ToList();
                var laneTrips = trips.Where(t => t.Lane == lane).ToList();
                var late = deliveries.Where(x => x.Lane == lane && x.OnTime == false && x.PromisedBy is not null && x.CompletedAt is not null).Select(x => Minutes(x.CompletedAt, x.PromisedBy)!.Value).ToList();
                var laneRatings = ratings.Where(r => r.Lane == lane).ToList();
                var k = kpis[lane];
                rows.Add(new ReportRow
                {
                    ["lane"] = lane, ["region"] = mine[0].Region, ["shipments"] = mine.Count, ["spend"] = k["FREIGHT_SPEND"].Value, ["costPerShipment"] = k["COST_PER_SHIPMENT"].Value,
                    ["costPerKm"] = Div(priced.Sum(s => s.FreightEstimate!.Value), priced.Sum(s => s.DistanceKm!.Value)), ["otp"] = k["OTP"].Value, ["otd"] = k["OTD"].Value, ["podCompliance"] = k["POD_COMPLIANCE"].Value,
                    ["weightUtil"] = k["WEIGHT_UTIL"].Value, ["volumeUtil"] = k["VOLUME_UTIL"].Value, ["avgDelayMin"] = late.Count == 0 ? null : Math.Round(late.Average(), 0),
                    ["routeDeviationKm"] = laneTrips.Count == 0 ? null : Math.Round(laneTrips.Sum(t => t.DeviationKm), 1), ["avgDwellMin"] = laneTrips.Count == 0 ? null : Math.Round((decimal)laneTrips.Average(t => t.DwellMinutes), 0),
                    ["topTransporter"] = mine.Where(s => s.TransporterName is not null).GroupBy(s => s.TransporterName!).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(),
                    ["contractedRate"] = laneRatings.Count == 0 ? null : Math.Round(laneRatings.Average(r => r.BaseFreight), 0), ["actualAverage"] = laneRatings.Count == 0 ? k["COST_PER_SHIPMENT"].Value : Math.Round(laneRatings.Average(r => r.FinalFreight), 0),
                });
            }

            var by = ctx.Filters.Get("rankBy") is { } rb && ctx.Columns.Any(c => c.Equals(rb, StringComparison.OrdinalIgnoreCase)) ? rb : "spend";
            var lowerBetter = by is "costPerShipment" or "costPerKm" or "avgDelayMin" or "routeDeviationKm" or "avgDwellMin";
            var ordered = (lowerBetter ? rows.Where(r => r[by] is not null).OrderBy(r => Num(r[by])) : rows.Where(r => r[by] is not null).OrderByDescending(r => Num(r[by]))).Concat(rows.Where(r => r[by] is null)).ToList();
            var rank = 0;
            foreach (var r in ordered)
            {
                r["rank"] = r[by] is null ? null : ++rank;
                d.Rows.Add(r);
            }

            d.Charts.Add(Chart("lane-spend", "Freight spend by lane", "bar", "lane", d.Rows.Where(r => r["spend"] is not null).OrderByDescending(r => Num(r["spend"])).Take(ctx.Settings.TopN).Select(r => new ReportRow { ["lane"] = r["lane"], ["spend"] = r["spend"] }), new ChartSeries("spend", "Freight spend")));
            d.Charts.Add(new ChartData("lane-heat", "How lanes score (higher is better)", "heatmap", "metric", [new ChartSeries("value", "Value")],
                d.Rows.Take(15).SelectMany(r => new[] { ("OTD %", "otd"), ("OTP %", "otp"), ("POD %", "podCompliance"), ("Weight util %", "weightUtil") }.Select(m => new ReportRow { ["lane"] = r["lane"], ["metric"] = m.Item1, ["value"] = r[m.Item2] })).ToList(),
                "Blank cells are measures that cannot be judged for that lane."));
            if (shipments.Select(s => s.Lane).Distinct().Count() > lanes.Count)
            {
                d.Notes.Add($"The {lanes.Count} lanes with the most shipments are shown.");
            }

            d.Notes.Add($"Ranked by {by}{(lowerBetter ? " (lower is better)" : " (higher is better)")}. Contracted rate is the base freight of the kept ratings before diesel and extra charges; it is shown only to people who may see commercial data.");
            return d;
        },
    };

    // ---- R38

    private static ReportSpec CostVsPerformance() => new()
    {
        Code = "R38_COST_VS_PERFORMANCE", Name = "Transporter cost vs performance", Category = CatCross, Type = ReportType.Analytical, DataSource = "All modules", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Cross, SortOrder = 380, ExportFormats = "csv,xlsx,pdf",
        Description = "Each carrier placed by cost per shipment and by performance, and classed low or high on each, so cheap-and-good, cheap-and-poor, dear-and-good and dear-and-poor carriers are easy to see.",
        Columns = Cols(
            C("transporter", "Transporter", filter: true), C("quadrant", "Class", FieldType.Status), C("shipments", "Shipments", FieldType.Whole), C("costPerShipment", "Cost / shipment", FieldType.Currency), C("otd", "OTD %", FieldType.Percent),
            C("score", "Score", FieldType.Number), C("claims", "Claims %", FieldType.Percent), C("placement", "Placement %", FieldType.Percent), C("performance", "Performance used", FieldType.Number)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Lane, FilterNames.Region, FilterNames.Customer, FilterNames.ServiceType),
        Drills = [new("transporter", "R09_TRANSPORTER_SCORECARD", "Scorecard", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("costPerShipment"),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData { Fixed = true };
            var (carriers, costLine, perfLine) = await Carriers(ctx);
            foreach (var c in carriers.OrderBy(c => c.Quadrant).ThenBy(c => c.CostPerShipment))
            {
                d.Rows.Add(new ReportRow
                {
                    ["transporter"] = c.Name, ["quadrant"] = c.Quadrant, ["shipments"] = c.Shipments, ["costPerShipment"] = c.CostPerShipment, ["otd"] = c.Otd, ["score"] = c.Score, ["claims"] = c.Claims, ["placement"] = c.Placement, ["performance"] = c.Performance,
                });
            }

            d.Charts.Add(Scatter(ctx, carriers, costLine, perfLine));
            foreach (var q in Quadrants)
            {
                d.Totals.Add(new(q, q, carriers.Count(c => c.Quadrant == q), null, null, null, q.StartsWith("Low cost / High", StringComparison.Ordinal) ? "good" : q.StartsWith("High cost / Low", StringComparison.Ordinal) ? "bad" : null));
            }

            d.Totals.Add(new("unclassified", "Not classified", carriers.Count(c => c.Quadrant == "Not classified")));
            d.Notes.Add("A carrier is classed only when both its cost per shipment and its performance can be measured. The dividing lines move with the carriers selected.");
            return d;
        },
    };

    // ---- R39

    private static ReportSpec CostVsService() => new()
    {
        Code = "R39_COST_VS_SERVICE", Name = "Cost vs service analysis", Category = CatCross, Type = ReportType.Analytical, DataSource = "All modules", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Cross, SortOrder = 390, ExportFormats = "csv,xlsx,pdf",
        Description = "Freight cost set against service quality (OTD, OTP, POD, claims, ETA accuracy, exceptions) by transporter, lane, vehicle type, customer or region, to answer whether lower cost is being bought with worse service.",
        Columns = Cols(
            C("group", "Group", filter: true), C("shipments", "Shipments", FieldType.Whole), C("freightCost", "Freight cost", FieldType.Currency), C("costPerShipment", "Cost / shipment", FieldType.Currency), C("costPerTon", "Cost / ton", FieldType.Currency),
            C("otd", "OTD %", FieldType.Percent), C("otp", "OTP %", FieldType.Percent), C("pod", "POD compliance %", FieldType.Percent), C("claims", "Claims %", FieldType.Percent), C("etaAccuracy", "ETA accuracy %", FieldType.Percent), C("exceptions", "Exceptions", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, "by", FilterNames.Lane, FilterNames.Region, FilterNames.Customer, FilterNames.ServiceType, FilterNames.Transporter),
        Drills = [new("group", "R09_TRANSPORTER_SCORECARD", "Scorecard", Map((FilterNames.Transporter, "group")))],
        Sorts = Sort("costPerShipment"),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData { Fixed = true };
            var by = (ctx.Filters.Get("by") ?? "transporter").ToLowerInvariant();
            var (filter, selector) = by switch
            {
                "lane" => (FilterNames.Lane, (Func<ShipmentReportFact, string?>)(s => s.Lane)),
                "vehicletype" => (FilterNames.VehicleType, s => s.VehicleType),
                "customer" => (FilterNames.Customer, s => s.Customer),
                "region" => (FilterNames.Region, s => s.Region),
                _ => (FilterNames.Transporter, s => s.TransporterName),
            };
            var shipments = (await ctx.Facts.Shipments()).Where(s => !s.IsCancelled).ToList();
            var groups = shipments.Select(selector).Where(v => v is not null).Select(v => v!).GroupBy(v => v, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Take(40).Select(g => g.Key).ToList();
            var open = ctx.Facts.Without(filter);
            var k = await KpisBy(open, filter, groups, ["FREIGHT_SPEND", "COST_PER_SHIPMENT", "COST_PER_TON", "OTD", "OTP", "POD_COMPLIANCE", "CLAIMS_RATE", "ETA_ACCURACY"]);
            foreach (var g in groups)
            {
                var exceptions = (await open.With(filter, g).Exceptions(anyDate: false)).Count;
                var kk = k[g];
                d.Rows.Add(new ReportRow
                {
                    ["group"] = g, ["shipments"] = shipments.Count(s => string.Equals(selector(s), g, StringComparison.OrdinalIgnoreCase)), ["freightCost"] = kk["FREIGHT_SPEND"].Value, ["costPerShipment"] = kk["COST_PER_SHIPMENT"].Value,
                    ["costPerTon"] = kk["COST_PER_TON"].Value, ["otd"] = kk["OTD"].Value, ["otp"] = kk["OTP"].Value, ["pod"] = kk["POD_COMPLIANCE"].Value, ["claims"] = kk["CLAIMS_RATE"].Value, ["etaAccuracy"] = kk["ETA_ACCURACY"].Value, ["exceptions"] = exceptions,
                });
            }

            var pairs = d.Rows.Where(r => r["costPerShipment"] is not null && r["otd"] is not null).Select(r => ((decimal)r["costPerShipment"]!, (decimal)r["otd"]!)).ToList();
            var r = Correlation(pairs);
            string verdict;
            if (pairs.Count < 4)
            {
                verdict = $"Too few groups with both cost and OTD ({pairs.Count}) to say whether cost and service move together.";
            }
            else
            {
                var median = Median(pairs.Select(p => p.Item1))!.Value;
                var cheap = pairs.Where(p => p.Item1 <= median).Select(p => p.Item2).ToList();
                var dear = pairs.Where(p => p.Item1 > median).Select(p => p.Item2).ToList();
                var gap = cheap.Count == 0 || dear.Count == 0 ? (decimal?)null : Math.Round(cheap.Average() - dear.Average(), 1);
                verdict = gap switch
                {
                    null => "Cost and OTD do not vary enough across these groups to compare.",
                    <= -2m => $"Yes: the cheaper half delivers on time {-gap:0.#} points less often than the dearer half (OTD {cheap.Average():0.#}% against {dear.Average():0.#}%). Lower cost is coming with weaker service.",
                    >= 2m => $"No: the cheaper half delivers on time {gap:0.#} points more often than the dearer half (OTD {cheap.Average():0.#}% against {dear.Average():0.#}%). Lower cost is not being bought with worse service.",
                    _ => $"No clear difference: the cheaper and dearer halves are within 2 points on OTD ({cheap.Average():0.#}% against {dear.Average():0.#}%).",
                };
                verdict += r is null ? string.Empty : $" Correlation of cost per shipment with OTD across {pairs.Count} groups: {r:0.00} (a pattern, not proof of cause).";
            }

            d.Sections.Add(new("answer", "Is lower cost coming at the expense of service?", null, [new("Reading", verdict), new("Compared by", by), new("Groups compared", pairs.Count)]));
            d.Charts.Add(new ChartData("cost-service", $"Cost per shipment against OTD, by {by}", "scatter", "cost", [new ChartSeries("group", "Group")],
                d.Rows.Where(x => x["costPerShipment"] is not null && x["otd"] is not null).Select(x => new ReportRow { ["cost"] = x["costPerShipment"], ["performance"] = x["otd"], ["label"] = x["group"], ["quadrant"] = "Group" }).ToList(),
                "A pattern across groups, not proof that cost causes service.", "OTD %", "Cost per shipment (₹)"));
            d.Notes.Add("Groups with nothing to judge on a measure show it blank, not zero.");
            return d;
        },
    };
}
