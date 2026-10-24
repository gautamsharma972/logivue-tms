using Tms.SharedKernel.Contracts;
using static Tms.Modules.Reports.Domain.Kit;

namespace Tms.Modules.Reports.Domain;

/// <summary>
/// R40–R57: the transporter's own operations, from the request to the payment. Every figure is read from Transporter Operations; a time or an amount that was never recorded is blank, never zero.
/// A report that needs a customer is limited to the customers the caller may see.
/// </summary>
internal static class OperationsReports
{
    private static readonly int[] AgeBounds = [1, 3, 7, 15, 30];

    public static IEnumerable<ReportSpec> All()
    {
        yield return RequestRegister();
        yield return LrRegister();
        yield return DispatchRegister();
        yield return LoadingDuration();
        yield return UnloadingDuration();
        yield return DeliveryCompletion();
        yield return PendingPod();
        yield return PodAgeing();
        yield return PodRejections();
        yield return FreightRegister();
        yield return CustomerFreight();
        yield return DetentionReport();
        yield return DetentionByCustomer();
        yield return DeliveryCharges();
        yield return InvoiceRegister();
        yield return SubmissionReport();
        yield return Receivables();
        yield return VehicleUtilisation();
    }

    private static readonly string[] Dimensions = ["customer"];

    private static IReadOnlyList<FilterSpec> LrFilters(params string[] extra) =>
        Filters([FilterNames.FromDate, FilterNames.ToDate, FilterNames.Customer, .. extra]);

    private static ReportSpec Make(string code, string name, string description, int order, IReadOnlyList<ColumnSpec> columns, IReadOnlyList<FilterSpec> filters, IReadOnlyList<GroupingSpec> groups, IReadOnlyList<MeasureSpec> measures,
        IReadOnlyList<SortSpec> sorts, Func<ReportContext, Task<ReportData>> build, string source = "Transporter Operations") => new()
    {
        Code = code, Name = name, Description = description, Category = CatOps, Type = ReportType.Operational, DataSource = source, Refresh = RefreshType.NearRealTime, Permission = ReportingPermissions.Operations,
        SortOrder = 400 + order, ExportFormats = "csv,xlsx,pdf", Columns = columns, Filters = filters, Groupings = groups, Measures = measures, Sorts = sorts, ScopeDimensions = Dimensions,
        Build = async (ctx, ct) => await build(ctx),
    };

    private static decimal? Hours(DateTimeOffset? later, DateTimeOffset? earlier) => Minutes(later, earlier) is { } m ? Math.Round(m / 60m, 1) : null;

    private static int? AgeDays(OpsLrFact l, DateTimeOffset now) => l.UnloadedAt is { } u ? Math.Max(0, (int)(now - u).TotalDays) : null;

    private static ReportRow Base(OpsLrFact l) => new()
    {
        ["lr"] = l.LrNumber, ["customer"] = l.Customer, ["po"] = l.CustomerReference, ["cfa"] = l.Cfa, ["destination"] = l.Destination, ["city"] = l.DestinationCity, ["vehicle"] = l.Vehicle, ["trip"] = l.Trip, ["date"] = l.Date,
        ["month"] = Bucket(l.Date, "month"), ["status"] = l.Status, ["weight"] = l.WeightKg, ["lrs"] = 1,
    };

    private static ColumnSpec[] LrCols => [C("lr", "LR", filter: true), C("customer", "Customer", filter: true), C("po", "Customer PO"), C("cfa", "CFA"), C("destination", "Destination"), C("vehicle", "Vehicle", filter: true), C("date", "Date", FieldType.Date), C("month", "Month")];

    // ---------------------------------------------------------------- requests, LRs, dispatch
    private static ReportSpec RequestRegister() => Make("R40_REQUEST_REGISTER", "Delivery request register", "Customer requests that were turned into LRs: when each was received, when its LR was made, and how long dispatch took to react.", 0,
        Cols([C("request", "Request", filter: true), C("customer", "Customer", filter: true), C("po", "Customer PO"), C("receivedAt", "Received", FieldType.DateTime), C("lr", "LR"), C("lrAt", "LR created", FieldType.DateTime), C("lagH", "Request to LR (h)", FieldType.Number), C("destination", "Destination"), C("weight", "Weight (kg)", FieldType.Number), C("requests", "Requests", FieldType.Whole), C("month", "Month")]),
        LrFilters(), Groups(("customer", "Customer"), ("month", "Month")), [new("requests", MeasureKind.Sum), new("lagH", MeasureKind.Avg), new("weight", MeasureKind.Sum)], Sort("receivedAt", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.RequestNumber is not null).ToList();
            foreach (var l in rows)
            {
                d.Rows.Add(new ReportRow { ["request"] = l.RequestNumber, ["customer"] = l.Customer, ["po"] = l.CustomerReference, ["receivedAt"] = l.RequestReceivedAt, ["lr"] = l.LrNumber, ["lrAt"] = l.CreatedAt, ["lagH"] = Hours(l.CreatedAt, l.RequestReceivedAt),
                    ["destination"] = l.Destination, ["weight"] = l.WeightKg, ["requests"] = 1, ["month"] = Bucket(l.Date, "month") });
            }

            d.Totals.Add(new("requests", "Requests converted", rows.Count));
            d.Notes.Add("Requests that are still being checked, or were cancelled, have no LR yet and are not listed; they are on the Delivery requests page.");
            return d;
        });

    private static ReportSpec LrRegister() => Make("R41_LR_REGISTER", "LR register", "Every LR with its customer, destination, vehicle, status and the proof of delivery status.", 1,
        Cols([.. LrCols, C("status", "Delivery", FieldType.Status, filter: true), C("pod", "POD", FieldType.Status), C("weight", "Weight (kg)", FieldType.Number), C("packages", "Packages", FieldType.Whole), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(FilterNames.Status), Groups(("customer", "Customer"), ("destination", "Destination"), ("status", "Delivery status"), ("month", "Month")), [new("lrs", MeasureKind.Sum), new("weight", MeasureKind.Sum), new("packages", MeasureKind.Sum)], Sort("date", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = await ctx.Facts.OpsLrs();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["pod"] = l.PodStatus;
                r["packages"] = l.Packages;
                d.Rows.Add(r);
            }

            d.Totals.Add(new("lrs", "LRs", rows.Count));
            d.Totals.Add(new("weight", "Weight (kg)", rows.Sum(l => l.WeightKg), "number"));
            d.Charts.Add(Chart("status", "LRs by delivery status", "donut", "status", rows.GroupBy(l => l.Status).Select(g => new ReportRow { ["status"] = g.Key, ["lrs"] = g.Count() }), new ChartSeries("lrs", "LRs")));
            return d;
        });

    private static ReportSpec DispatchRegister() => Make("R42_DISPATCH_REGISTER", "Vehicle dispatch register", "Each vehicle's departures from the CFA, with the LRs, weight and how long the vehicle was at the CFA.", 2,
        Cols([C("trip", "Trip", filter: true), C("vehicle", "Vehicle", filter: true), C("cfa", "CFA"), C("customer", "Customer", filter: true), C("lr", "LR"), C("departedAt", "Departed", FieldType.DateTime), C("atCfaMin", "At CFA (min)", FieldType.Number), C("weight", "Weight (kg)", FieldType.Number), C("lrs", "LRs", FieldType.Whole), C("month", "Month")]),
        LrFilters(FilterNames.Vehicle), Groups(("vehicle", "Vehicle"), ("customer", "Customer"), ("month", "Month")), [new("lrs", MeasureKind.Sum), new("weight", MeasureKind.Sum), new("atCfaMin", MeasureKind.Avg)], Sort("departedAt", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.DepartedAt is not null).ToList();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["departedAt"] = l.DepartedAt;
                r["atCfaMin"] = Minutes(l.DepartedAt, l.CfaReachedAt);
                d.Rows.Add(r);
            }

            d.Totals.Add(new("departed", "LRs dispatched", rows.Count));
            return d;
        });

    private static ReportSpec LoadingDuration() => Make("R43_CFA_LOADING", "CFA loading duration", "How long vehicles wait for loading and how long loading takes at each CFA. Times that were not recorded are left out, not counted as zero.", 3,
        Cols([C("trip", "Trip", filter: true), C("cfa", "CFA", filter: true), C("vehicle", "Vehicle", filter: true), C("customer", "Customer", filter: true), C("reachedAt", "Reached CFA", FieldType.DateTime), C("waitMin", "Wait before loading (min)", FieldType.Number),
            C("loadingMin", "Loading (min)", FieldType.Number), C("departWaitMin", "Loaded to departure (min)", FieldType.Number), C("turnMin", "Total at CFA (min)", FieldType.Number), C("lrs", "LRs", FieldType.Whole), C("month", "Month")]),
        LrFilters(FilterNames.Vehicle), Groups(("cfa", "CFA"), ("vehicle", "Vehicle"), ("month", "Month")), [new("loadingMin", MeasureKind.Avg), new("waitMin", MeasureKind.Avg), new("turnMin", MeasureKind.Avg), new("lrs", MeasureKind.Sum)], Sort("reachedAt", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.CfaReachedAt is not null).ToList();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["reachedAt"] = l.CfaReachedAt;
                r["waitMin"] = Minutes(l.LoadingStartedAt, l.CfaReachedAt);
                r["loadingMin"] = Minutes(l.LoadingCompletedAt, l.LoadingStartedAt);
                r["departWaitMin"] = Minutes(l.DepartedAt, l.LoadingCompletedAt);
                r["turnMin"] = Minutes(l.DepartedAt, l.CfaReachedAt);
                d.Rows.Add(r);
            }

            var loading = rows.Select(l => Minutes(l.LoadingCompletedAt, l.LoadingStartedAt)).Where(m => m is not null).Select(m => m!.Value).ToList();
            d.Totals.Add(new("avgLoading", "Average loading (min)", loading.Count == 0 ? null : Math.Round(loading.Average(), 0), "number"));
            d.Totals.Add(new("median", "Median loading (min)", Median(loading), "number"));
            return d;
        });

    private static ReportSpec UnloadingDuration() => Make("R44_DESTINATION_WAIT", "Destination arrival and unloading duration", "From the driver's confirmed arrival to unloading completed, by destination. This is the time detention is measured on.", 4,
        Cols([C("lr", "LR", filter: true), C("customer", "Customer", filter: true), C("destination", "Destination", filter: true), C("vehicle", "Vehicle"), C("departedAt", "Left CFA", FieldType.DateTime), C("arrivedAt", "Arrived", FieldType.DateTime), C("unloadedAt", "Unloaded", FieldType.DateTime),
            C("transitH", "Transit (h)", FieldType.Number), C("waitMin", "Arrival to unloaded (min)", FieldType.Number), C("lrs", "LRs", FieldType.Whole), C("month", "Month")]),
        LrFilters(), Groups(("destination", "Destination"), ("customer", "Customer"), ("month", "Month")), [new("waitMin", MeasureKind.Avg), new("transitH", MeasureKind.Avg), new("lrs", MeasureKind.Sum)], Sort("arrivedAt", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.ArrivedAt is not null).ToList();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["departedAt"] = l.DepartedAt;
                r["arrivedAt"] = l.ArrivedAt;
                r["unloadedAt"] = l.UnloadedAt;
                r["transitH"] = Hours(l.ArrivedAt, l.DepartedAt);
                r["waitMin"] = Minutes(l.UnloadedAt, l.ArrivedAt);
                d.Rows.Add(r);
            }

            var waits = rows.Select(l => Minutes(l.UnloadedAt, l.ArrivedAt)).Where(m => m is not null).Select(m => m!.Value).ToList();
            d.Totals.Add(new("avgWait", "Average wait at destination (min)", waits.Count == 0 ? null : Math.Round(waits.Average(), 0), "number"));
            d.Totals.Add(new("median", "Median (min)", Median(waits), "number"));
            d.Notes.Add("Arrival is the time the driver's phone location was accepted at the gate, or the time a supervisor recorded with a reason.");
            return d;
        });

    private static ReportSpec DeliveryCompletion() => Make("R45_DELIVERY_COMPLETION", "Delivery completion register", "Completed deliveries with arrival, unloading and the state of the proof of delivery. A delivery can be complete while its POD is still pending.", 5,
        Cols([.. LrCols, C("unloadedAt", "Unloaded", FieldType.DateTime), C("pod", "POD", FieldType.Status, filter: true), C("lrs", "LRs", FieldType.Whole), C("weight", "Weight (kg)", FieldType.Number)]),
        LrFilters(), Groups(("customer", "Customer"), ("destination", "Destination"), ("pod", "POD status"), ("month", "Month")), [new("lrs", MeasureKind.Sum), new("weight", MeasureKind.Sum)], Sort("unloadedAt", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.UnloadedAt is not null).ToList();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["unloadedAt"] = l.UnloadedAt;
                r["pod"] = l.PodStatus;
                d.Rows.Add(r);
            }

            d.Totals.Add(new("done", "Completed deliveries", rows.Count));
            d.Totals.Add(new("accepted", "POD accepted", rows.Count(l => l.PodStatus == "Accepted")));
            d.Charts.Add(Chart("pod", "POD status of completed deliveries", "donut", "pod", rows.GroupBy(l => l.PodStatus).Select(g => new ReportRow { ["pod"] = g.Key, ["lrs"] = g.Count() }), new ChartSeries("lrs", "LRs")));
            return d;
        });

    // ---------------------------------------------------------------- proof of delivery
    private static ReportSpec PendingPod() => Make("R46_PENDING_POD", "Pending POD report", "Delivered consignments whose proof of delivery has not been received, or was rejected. Oldest first.", 6,
        Cols([.. LrCols, C("unloadedAt", "Unloaded", FieldType.DateTime), C("pod", "POD", FieldType.Status, filter: true), C("ageDays", "Waiting (days)", FieldType.Whole), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(), Groups(("customer", "Customer"), ("destination", "Destination"), ("pod", "POD status")), [new("lrs", MeasureKind.Sum), new("ageDays", MeasureKind.Avg)], Sort("ageDays", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.UnloadedAt is not null && l.PodStatus is "NotYetDue" or "Pending" or "Rejected").ToList();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["unloadedAt"] = l.UnloadedAt;
                r["pod"] = l.PodStatus;
                r["ageDays"] = AgeDays(l, ctx.Facts.Now);
                d.Rows.Add(r);
            }

            d.Totals.Add(new("pending", "Waiting for POD", rows.Count(l => l.PodStatus != "Rejected")));
            d.Totals.Add(new("rejected", "Rejected, waiting for a correction", rows.Count(l => l.PodStatus == "Rejected")));
            return d;
        });

    private static ReportSpec PodAgeing() => Make("R47_POD_AGEING_OPS", "POD ageing report", "How long delivered consignments have waited for an accepted proof of delivery, in age bands.", 7,
        Cols([.. LrCols, C("unloadedAt", "Unloaded", FieldType.DateTime), C("pod", "POD", FieldType.Status), C("ageDays", "Age (days)", FieldType.Whole), C("band", "Age band", filter: true), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(), Groups(("band", "Age band"), ("customer", "Customer"), ("destination", "Destination")), [new("lrs", MeasureKind.Sum), new("ageDays", MeasureKind.Avg)], Sort("ageDays", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.UnloadedAt is not null && l.PodStatus != "Accepted").ToList();
            foreach (var l in rows)
            {
                var age = AgeDays(l, ctx.Facts.Now) ?? 0;
                var r = Base(l);
                r["unloadedAt"] = l.UnloadedAt;
                r["pod"] = l.PodStatus;
                r["ageDays"] = age;
                r["band"] = AgeBucket(age, AgeBounds);
                d.Rows.Add(r);
            }

            d.Charts.Add(Chart("bands", "Proofs outstanding by age", "bar", "band", rows.GroupBy(l => AgeBucket(AgeDays(l, ctx.Facts.Now) ?? 0, AgeBounds)).Select(g => new ReportRow { ["band"] = g.Key, ["lrs"] = g.Count() }), new ChartSeries("lrs", "LRs")));
            d.Totals.Add(new("outstanding", "Proofs outstanding", rows.Count));
            return d;
        });

    private static ReportSpec PodRejections() => Make("R48_POD_REJECTIONS_OPS", "POD rejection report", "Proofs that were rejected at least once, with how many times and where the documents came from (driver or office).", 8,
        Cols([.. LrCols, C("pod", "POD now", FieldType.Status), C("rejections", "Times rejected", FieldType.Whole), C("source", "First upload by"), C("reviewedAt", "Last reviewed", FieldType.DateTime), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(), Groups(("customer", "Customer"), ("source", "First upload by"), ("month", "Month")), [new("lrs", MeasureKind.Sum), new("rejections", MeasureKind.Sum)], Sort("rejections", true),
        async ctx =>
        {
            var d = new ReportData();
            var all = (await ctx.Facts.OpsLrs()).Where(l => l.UnloadedAt is not null).ToList();
            var rows = all.Where(l => l.PodRejections > 0).ToList();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["pod"] = l.PodStatus;
                r["rejections"] = l.PodRejections;
                r["source"] = l.PodSource == "QrWeb" ? "Driver (QR)" : l.PodSource == "Office" ? "Office" : null;
                r["reviewedAt"] = l.PodReviewedAt;
                d.Rows.Add(r);
            }

            d.Totals.Add(new("rejected", "LRs with a rejected POD", rows.Count));
            d.Totals.Add(new("rate", "Share of delivered LRs", all.Count == 0 ? null : Math.Round(100m * rows.Count / all.Count, 1), "percent"));
            return d;
        });

    // ---------------------------------------------------------------- freight and detention
    private static ReportSpec FreightRegister() => Make("R49_FREIGHT_REGISTER", "Freight calculation register", "Freight worked out for each delivered LR, with its status and any hold that stops it being billed.", 9,
        Cols([.. LrCols, C("calc", "Calculation", FieldType.Status, filter: true), C("freight", "Basic freight (₹)", FieldType.Currency), C("other", "Other charges (₹)", FieldType.Currency), C("detention", "Detention (₹)", FieldType.Currency), C("total", "Total before tax (₹)", FieldType.Currency), C("hold", "Hold"), C("invoice", "Invoice"), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(FilterNames.Status), Groups(("customer", "Customer"), ("calc", "Calculation status"), ("month", "Month")), [new("freight", MeasureKind.Sum), new("other", MeasureKind.Sum), new("detention", MeasureKind.Sum), new("total", MeasureKind.Sum), new("lrs", MeasureKind.Sum)], Sort("date", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.UnloadedAt is not null).ToList();
            foreach (var l in rows)
            {
                d.Rows.Add(Charge(l));
            }

            d.Totals.Add(new("calculated", "Calculated", rows.Count(l => l.CalcStatus is not null)));
            d.Totals.Add(new("held", "On hold", rows.Count(l => l.CalcStatus == "OnHold"), null, null, null, rows.Any(l => l.CalcStatus == "OnHold") ? "bad" : null));
            d.Totals.Add(new("notCalc", "Not calculated yet", rows.Count(l => l.CalcStatus is null)));
            return d;
        });

    private static ReportRow Charge(OpsLrFact l)
    {
        var r = Base(l);
        r["calc"] = l.CalcStatus ?? "Not calculated";
        r["freight"] = l.BasicFreight;
        r["other"] = l.OtherCharges;
        r["detention"] = l.Detention;
        r["total"] = l.BasicFreight is null ? null : l.BasicFreight + (l.OtherCharges ?? 0) + (l.Detention ?? 0);
        r["hold"] = l.Hold;
        r["invoice"] = l.InvoiceNumber;
        return r;
    }

    private static ReportSpec CustomerFreight() => Make("R50_CUSTOMER_FREIGHT", "Customer-wise freight report", "Freight, detention and other charges by customer for the period: how much work was done and how much is billed.", 10,
        Cols([C("customer", "Customer", filter: true), C("lr", "LR"), C("month", "Month"), C("weight", "Weight (kg)", FieldType.Number), C("freight", "Basic freight (₹)", FieldType.Currency), C("other", "Other charges (₹)", FieldType.Currency), C("detention", "Detention (₹)", FieldType.Currency),
            C("total", "Total before tax (₹)", FieldType.Currency), C("billed", "Billed (₹)", FieldType.Currency), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(), Groups(("customer", "Customer"), ("month", "Month")), [new("lrs", MeasureKind.Sum), new("weight", MeasureKind.Sum), new("freight", MeasureKind.Sum), new("other", MeasureKind.Sum), new("detention", MeasureKind.Sum), new("total", MeasureKind.Sum), new("billed", MeasureKind.Sum)],
        Sort("total", true),
        async ctx =>
        {
            var d = new ReportData { Fixed = false };
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.BasicFreight is not null).ToList();
            foreach (var l in rows)
            {
                var r = Charge(l);
                r["billed"] = l.InvoiceNumber is null ? null : r["total"];
                d.Rows.Add(r);
            }

            d.Totals.Add(new("total", "Total before tax (₹)", rows.Sum(l => l.BasicFreight!.Value + (l.OtherCharges ?? 0) + (l.Detention ?? 0)), "currency"));
            d.Totals.Add(new("unbilled", "Not yet invoiced (₹)", rows.Where(l => l.InvoiceNumber is null).Sum(l => l.BasicFreight!.Value + (l.OtherCharges ?? 0) + (l.Detention ?? 0)), "currency"));
            d.Charts.Add(Chart("customers", "Charges by customer", "bar", "customer", rows.GroupBy(l => l.Customer).Select(g => new ReportRow { ["customer"] = g.Key, ["total"] = g.Sum(l => l.BasicFreight!.Value + (l.OtherCharges ?? 0) + (l.Detention ?? 0)) }), new ChartSeries("total", "₹ before tax")));
            return d;
        });

    private static ReportSpec DetentionReport() => Make("R51_DETENTION", "Detention calculation report", "Detention for each delivery: the time at the destination, the status of the calculation and any hold. A missing time is a hold, never zero.", 11,
        Cols([.. LrCols, C("arrivedAt", "Arrived", FieldType.DateTime), C("unloadedAt", "Unloaded", FieldType.DateTime), C("waitH", "At destination (h)", FieldType.Number), C("dstatus", "Detention", FieldType.Status, filter: true), C("detention", "Detention (₹)", FieldType.Currency), C("hold", "Hold"), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(), Groups(("customer", "Customer"), ("dstatus", "Detention status"), ("month", "Month")), [new("detention", MeasureKind.Sum), new("waitH", MeasureKind.Avg), new("lrs", MeasureKind.Sum)], Sort("detention", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.UnloadedAt is not null).ToList();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["arrivedAt"] = l.ArrivedAt;
                r["unloadedAt"] = l.UnloadedAt;
                r["waitH"] = Hours(l.UnloadedAt, l.ArrivedAt);
                r["dstatus"] = l.DetentionStatus ?? "Not calculated";
                r["detention"] = l.Detention;
                r["hold"] = l.Hold;
                d.Rows.Add(r);
            }

            d.Totals.Add(new("detention", "Detention charged (₹)", rows.Sum(l => l.Detention ?? 0), "currency"));
            d.Totals.Add(new("charged", "LRs with detention", rows.Count(l => l.Detention > 0)));
            d.Totals.Add(new("held", "Detention on hold", rows.Count(l => l.DetentionStatus == "OnHold"), null, null, null, rows.Any(l => l.DetentionStatus == "OnHold") ? "bad" : null));
            return d;
        });

    private static ReportSpec DetentionByCustomer() => Make("R52_DETENTION_CUSTOMER", "Detention charges by customer", "How much detention each customer is charged, and how often, so customer-specific rules can be reviewed.", 12,
        Cols([C("customer", "Customer", filter: true), C("lr", "LR"), C("month", "Month"), C("waitH", "At destination (h)", FieldType.Number), C("detention", "Detention (₹)", FieldType.Currency), C("charged", "LRs with detention", FieldType.Whole), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(), Groups(("customer", "Customer"), ("month", "Month")), [new("detention", MeasureKind.Sum), new("charged", MeasureKind.Sum), new("lrs", MeasureKind.Sum), new("waitH", MeasureKind.Avg)], Sort("detention", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.Detention is not null).ToList();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["waitH"] = Hours(l.UnloadedAt, l.ArrivedAt);
                r["detention"] = l.Detention;
                r["charged"] = One(l.Detention > 0);
                d.Rows.Add(r);
            }

            d.Charts.Add(Chart("customers", "Detention by customer", "bar", "customer", rows.GroupBy(l => l.Customer).Select(g => new ReportRow { ["customer"] = g.Key, ["detention"] = g.Sum(l => l.Detention ?? 0) }), new ChartSeries("detention", "₹")));
            return d;
        });

    private static ReportSpec DeliveryCharges() => Make("R53_DELIVERY_CHARGES", "Delivery-wise charges", "Every charge on every delivered LR on one line: freight, other charges and detention, and the invoice it went on.", 13,
        Cols([.. LrCols, C("freight", "Basic freight (₹)", FieldType.Currency), C("other", "Other charges (₹)", FieldType.Currency), C("detention", "Detention (₹)", FieldType.Currency), C("total", "Total before tax (₹)", FieldType.Currency), C("invoice", "Invoice", filter: true), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(), Groups(("customer", "Customer"), ("invoice", "Invoice")), [new("freight", MeasureKind.Sum), new("other", MeasureKind.Sum), new("detention", MeasureKind.Sum), new("total", MeasureKind.Sum), new("lrs", MeasureKind.Sum)], Sort("date", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.BasicFreight is not null).ToList();
            foreach (var l in rows)
            {
                d.Rows.Add(Charge(l));
            }

            d.Totals.Add(new("lrs", "Deliveries with charges", rows.Count));
            d.Totals.Add(new("total", "Total before tax (₹)", rows.Sum(l => l.BasicFreight!.Value + (l.OtherCharges ?? 0) + (l.Detention ?? 0)), "currency"));
            return d;
        });

    // ---------------------------------------------------------------- invoices and receivables
    private static ReportSpec InvoiceRegister() => Make("R54_INVOICE_REGISTER", "Invoice register", "Customer invoices with totals, tax, submission status and payment.", 14,
        Cols([C("invoice", "Invoice", filter: true), C("customer", "Customer", filter: true), C("date", "Date", FieldType.Date), C("due", "Due", FieldType.Date), C("month", "Month"), C("status", "Status", FieldType.Status, filter: true), C("submission", "Portal", FieldType.Status), C("subtotal", "Subtotal (₹)", FieldType.Currency),
            C("tax", "Tax (₹)", FieldType.Currency), C("total", "Total (₹)", FieldType.Currency), C("paid", "Paid (₹)", FieldType.Currency), C("outstanding", "Outstanding (₹)", FieldType.Currency), C("invoices", "Invoices", FieldType.Whole)]),
        LrFilters(FilterNames.Status), Groups(("customer", "Customer"), ("month", "Month"), ("status", "Status")), [new("invoices", MeasureKind.Sum), new("subtotal", MeasureKind.Sum), new("tax", MeasureKind.Sum), new("total", MeasureKind.Sum), new("paid", MeasureKind.Sum), new("outstanding", MeasureKind.Sum)], Sort("date", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsInvoices()).Where(i => i.Status != "Cancelled").ToList();
            foreach (var i in rows)
            {
                d.Rows.Add(new ReportRow { ["invoice"] = i.InvoiceNumber, ["customer"] = i.Customer, ["date"] = i.Date, ["due"] = i.DueDate, ["month"] = Bucket(i.Date, "month"), ["status"] = i.Status, ["submission"] = i.Submission, ["subtotal"] = i.Subtotal, ["tax"] = i.Tax,
                    ["total"] = i.Total, ["paid"] = i.Paid, ["outstanding"] = i.Status == "Issued" ? i.Total - i.Paid : null, ["invoices"] = 1 });
            }

            d.Totals.Add(new("invoiced", "Invoiced (₹)", rows.Sum(i => i.Total), "currency"));
            d.Totals.Add(new("paid", "Received (₹)", rows.Sum(i => i.Paid), "currency"));
            return d;
        });

    private static ReportSpec SubmissionReport() => Make("R55_INVOICE_SUBMISSION", "Invoice submission and rejection report", "Which invoices are waiting to be sent to the customer's portal, which were rejected and why, and how often each was resubmitted.", 15,
        Cols([C("invoice", "Invoice", filter: true), C("customer", "Customer", filter: true), C("date", "Date", FieldType.Date), C("total", "Total (₹)", FieldType.Currency), C("submission", "Portal status", FieldType.Status, filter: true), C("attempts", "Submissions", FieldType.Whole),
            C("lastAt", "Last submitted", FieldType.DateTime), C("reason", "Customer's rejection reason"), C("invoices", "Invoices", FieldType.Whole), C("rejected", "Rejected", FieldType.Whole)]),
        LrFilters(), Groups(("customer", "Customer"), ("submission", "Portal status")), [new("invoices", MeasureKind.Sum), new("rejected", MeasureKind.Sum), new("total", MeasureKind.Sum)], Sort("attempts", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsInvoices()).Where(i => i.Status == "Issued").ToList();
            foreach (var i in rows)
            {
                d.Rows.Add(new ReportRow { ["invoice"] = i.InvoiceNumber, ["customer"] = i.Customer, ["date"] = i.Date, ["total"] = i.Total, ["submission"] = i.Submission, ["attempts"] = i.SubmissionAttempts, ["lastAt"] = i.LastSubmittedAt, ["reason"] = i.LastRejection,
                    ["invoices"] = 1, ["rejected"] = One(i.Submission == "ResubmissionRequired") });
            }

            d.Totals.Add(new("toSend", "Waiting to be submitted", rows.Count(i => i.Submission == "NotSubmitted")));
            d.Totals.Add(new("rejected", "Rejected, to resubmit", rows.Count(i => i.Submission == "ResubmissionRequired"), null, null, null, rows.Any(i => i.Submission == "ResubmissionRequired") ? "bad" : null));
            d.Totals.Add(new("accepted", "Accepted by the customer", rows.Count(i => i.Submission == "Acknowledged")));
            return d;
        });

    private static ReportSpec Receivables() => Make("R56_RECEIVABLES", "Outstanding receivables and ageing", "What customers still owe, by how long it is overdue. Cancelled and unissued invoices are not receivables.", 16,
        Cols([C("invoice", "Invoice", filter: true), C("customer", "Customer", filter: true), C("date", "Date", FieldType.Date), C("due", "Due", FieldType.Date), C("daysOver", "Days overdue", FieldType.Whole), C("band", "Ageing", filter: true), C("total", "Total (₹)", FieldType.Currency),
            C("outstanding", "Outstanding (₹)", FieldType.Currency), C("invoices", "Invoices", FieldType.Whole)]),
        LrFilters(), Groups(("band", "Ageing"), ("customer", "Customer")), [new("outstanding", MeasureKind.Sum), new("total", MeasureKind.Sum), new("invoices", MeasureKind.Sum)], Sort("daysOver", true),
        async ctx =>
        {
            var d = new ReportData();
            var today = ctx.Facts.Today;
            var rows = (await ctx.Facts.OpsInvoices()).Where(i => i.Status == "Issued" && i.Total > i.Paid).ToList();
            string Band(int over) => over <= 0 ? "Not yet due" : over <= 30 ? "1–30 days" : over <= 60 ? "31–60 days" : over <= 90 ? "61–90 days" : "Over 90 days";
            foreach (var i in rows)
            {
                var over = today.DayNumber - i.DueDate.DayNumber;
                d.Rows.Add(new ReportRow { ["invoice"] = i.InvoiceNumber, ["customer"] = i.Customer, ["date"] = i.Date, ["due"] = i.DueDate, ["daysOver"] = Math.Max(0, over), ["band"] = Band(over), ["total"] = i.Total, ["outstanding"] = i.Total - i.Paid, ["invoices"] = 1 });
            }

            d.Totals.Add(new("outstanding", "Outstanding (₹)", rows.Sum(i => i.Total - i.Paid), "currency"));
            d.Totals.Add(new("overdue", "Overdue (₹)", rows.Where(i => i.DueDate < today).Sum(i => i.Total - i.Paid), "currency", null, null, rows.Any(i => i.DueDate < today) ? "bad" : null));
            d.Charts.Add(Chart("ageing", "Outstanding by ageing", "bar", "band", rows.GroupBy(i => Band(today.DayNumber - i.DueDate.DayNumber)).Select(g => new ReportRow { ["band"] = g.Key, ["outstanding"] = g.Sum(i => i.Total - i.Paid) }), new ChartSeries("outstanding", "₹")));
            return d;
        });

    private static ReportSpec VehicleUtilisation() => Make("R57_VEHICLE_UTILISATION", "Vehicle utilisation summary", "For each vehicle: trips, LRs, weight carried, and time spent at the CFA. Based only on what was recorded; there is no tracking of idle time.", 17,
        Cols([C("vehicle", "Vehicle", filter: true), C("trip", "Trip"), C("lr", "LR"), C("customer", "Customer", filter: true), C("date", "Date", FieldType.Date), C("month", "Month"), C("weight", "Weight (kg)", FieldType.Number), C("atCfaMin", "At CFA (min)", FieldType.Number), C("loadingMin", "Loading (min)", FieldType.Number), C("lrs", "LRs", FieldType.Whole)]),
        LrFilters(FilterNames.Vehicle), Groups(("vehicle", "Vehicle"), ("month", "Month")), [new("lrs", MeasureKind.Sum), new("weight", MeasureKind.Sum), new("atCfaMin", MeasureKind.Avg), new("loadingMin", MeasureKind.Avg)], Sort("weight", true),
        async ctx =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.OpsLrs()).Where(l => l.Vehicle is not null && l.DepartedAt is not null).ToList();
            foreach (var l in rows)
            {
                var r = Base(l);
                r["atCfaMin"] = Minutes(l.DepartedAt, l.CfaReachedAt);
                r["loadingMin"] = Minutes(l.LoadingCompletedAt, l.LoadingStartedAt);
                d.Rows.Add(r);
            }

            d.Totals.Add(new("vehicles", "Vehicles used", rows.Select(l => l.Vehicle).Distinct().Count()));
            d.Totals.Add(new("trips", "Trips", rows.Select(l => l.Trip).Where(t => t is not null).Distinct().Count()));
            return d;
        });
}
