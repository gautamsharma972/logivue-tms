using Tms.SharedKernel.Contracts;
using static Tms.Modules.Reports.Domain.Kit;

namespace Tms.Modules.Reports.Domain;

/// <summary>R17–R23: delivery and proof of delivery. Status, on-time and SLA judgements are the Deliveries module's; these reports count and list them.</summary>
internal static class PodReports
{
    public static IEnumerable<ReportSpec> All()
    {
        yield return DeliveryPerformance();
        yield return PodCompliance();
        yield return PodAgeing();
        yield return Rejections();
        yield return Discrepancies("R21_SHORTAGE", "Shortage report", "Shortage", 210, "R21");
        yield return Discrepancies("R22_DAMAGE", "Damage report", "Damage", 220, "R22");
        yield return FailedRefused();
    }

    private static ReportSpec DeliveryPerformance() => new()
    {
        Code = "R17_DELIVERY_PERFORMANCE", Name = "Delivery performance", Category = CatPod, Type = ReportType.Analytical, DataSource = "Deliveries", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Delivery, VendorSafe = true, SortOrder = 170, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Deliveries by outcome: on time, late, partly delivered, failed and refused, with OTD. Use the filters to list the late ones.",
        Columns = Cols(
            C("delivery", "Delivery", filter: true), C("shipment", "Shipment"), C("transporter", "Transporter", filter: true), C("customer", "Customer", filter: true), C("lane", "Lane", filter: true), C("month", "Month"),
            C("promisedBy", "Promised by", FieldType.DateTime), C("completedAt", "Completed at", FieldType.DateTime), C("status", "Status", FieldType.Status, filter: true), C("onTime", "On time", FieldType.Status), C("delayMin", "Delay (min)", FieldType.Whole),
            C("deliveries", "Deliveries", FieldType.Whole), C("onTimeCount", "On-time count", FieldType.Whole), C("late", "Late", FieldType.Whole), C("partial", "Partially delivered", FieldType.Whole), C("failed", "Failed", FieldType.Whole), C("refused", "Refused", FieldType.Whole),
            C("judged", "Judged", FieldType.Whole, visible: false), C("otd", "OTD %", FieldType.Percent)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Customer, FilterNames.Lane, FilterNames.Region, FilterNames.Status, "onTime", FilterNames.Shipment).Select(f => f.Name == FilterNames.Status ? f.With("deliveryStatus") : f).ToList(),
        Groupings = Groups(("transporter", "Transporter"), ("customer", "Customer"), ("lane", "Lane"), ("status", "Status"), ("month", "Month")),
        Measures =
        [
            new("deliveries", MeasureKind.Count), new("onTimeCount", MeasureKind.Sum), new("late", MeasureKind.Sum), new("partial", MeasureKind.Sum), new("failed", MeasureKind.Sum), new("refused", MeasureKind.Sum), new("judged", MeasureKind.Sum),
            new("otd", MeasureKind.Ratio, "onTimeCount", "judged", 100m), new("delayMin", MeasureKind.Avg),
        ],
        Drills = [new("delivery", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment"))), new("late", "R17_DELIVERY_PERFORMANCE", "Late deliveries", Map(("onTime", "=No")))],
        Sorts = Sort("completedAt", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var want = ctx.Filters.Get("onTime");
            var rows = (await ctx.Facts.Deliveries()).Where(x => want is null || (want.Equals("No", StringComparison.OrdinalIgnoreCase) ? x.OnTime == false : want.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? x.OnTime == true : true)).ToList();
            foreach (var x in rows)
            {
                var done = x.Status is "Delivered" or "PartiallyDelivered";
                var judged = done && x.OnTime is not null;
                d.Rows.Add(new ReportRow
                {
                    ["delivery"] = x.DeliveryRef, ["shipment"] = x.ShipmentRef, ["transporter"] = x.TransporterName, ["customer"] = x.Customer, ["lane"] = x.Lane, ["month"] = Bucket(x.Date, "month"), ["promisedBy"] = x.PromisedBy,
                    ["completedAt"] = x.CompletedAt, ["status"] = x.Status, ["onTime"] = done ? Yn(x.OnTime) : "—", ["delayMin"] = x.OnTime == false ? Math.Max(0m, Minutes(x.CompletedAt, x.PromisedBy) ?? 0m) : null,
                    ["deliveries"] = 1, ["onTimeCount"] = One(judged && x.OnTime == true), ["late"] = One(judged && x.OnTime == false), ["partial"] = One(x.Status == "PartiallyDelivered"), ["failed"] = One(x.Status == "Failed"),
                    ["refused"] = One(x.Status == "Refused"), ["judged"] = One(judged),
                });
            }

            d.Cards.Add(await ctx.CardAsync("OTD"));
            d.Totals.Add(new("deliveries", "Deliveries", rows.Count));
            d.Totals.Add(new("late", "Late", rows.Count(x => x.OnTime == false && x.Status is "Delivered" or "PartiallyDelivered"), null, "R17_DELIVERY_PERFORMANCE", Map(("onTime", "=No"))));
            d.Totals.Add(new("partial", "Partially delivered", rows.Count(x => x.Status == "PartiallyDelivered")));
            d.Totals.Add(new("failed", "Failed", rows.Count(x => x.Status == "Failed"), null, "R23_FAILED_REFUSED", null));
            d.Totals.Add(new("refused", "Refused", rows.Count(x => x.Status == "Refused"), null, "R23_FAILED_REFUSED", null));
            d.Charts.Add(Chart("status", "Deliveries by outcome", "donut", "status", rows.GroupBy(x => x.Status).Select(g => new ReportRow { ["status"] = g.Key, ["deliveries"] = g.Count() }), new ChartSeries("deliveries", "Deliveries")));
            d.Notes.Add("On time is the Deliveries module's own judgement. A delivery with no planned time shows 'Not measurable' and is left out of OTD.");
            return d;
        },
    };

    private static ReportSpec PodCompliance() => new()
    {
        Code = "R18_POD_COMPLIANCE", Name = "POD compliance", Category = CatPod, Type = ReportType.Analytical, DataSource = "Deliveries (POD)", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Delivery, VendorSafe = true, SortOrder = 180, ExportFormats = "csv,xlsx,pdf", Comparison = true,
        Description = "Proofs of delivery required, submitted, pending, accepted and rejected, and how many were submitted within the SLA.",
        Columns = Cols(
            C("pod", "POD", filter: true), C("delivery", "Delivery"), C("shipment", "Shipment"), C("transporter", "Transporter", filter: true), C("customer", "Customer", filter: true), C("month", "Month"), C("status", "Status", FieldType.Status, filter: true),
            C("required", "POD required", FieldType.Whole), C("submitted", "Submitted", FieldType.Whole), C("pending", "Pending", FieldType.Whole), C("accepted", "Accepted", FieldType.Whole), C("rejected", "Rejected", FieldType.Whole),
            C("withinSla", "Within SLA", FieldType.Whole, visible: false), C("judged", "Judged", FieldType.Whole, visible: false), C("compliance", "Compliance %", FieldType.Percent)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Customer, FilterNames.Status, FilterNames.Shipment).Select(f => f.Name == FilterNames.Status ? f.With("podStatus") : f).ToList(),
        Groupings = Groups(("month", "Month"), ("transporter", "Transporter"), ("customer", "Customer"), ("status", "Status")),
        DefaultGroupBy = ["month"],
        Measures =
        [
            new("required", MeasureKind.Sum), new("submitted", MeasureKind.Sum), new("pending", MeasureKind.Sum), new("accepted", MeasureKind.Sum), new("rejected", MeasureKind.Sum), new("withinSla", MeasureKind.Sum),
            new("judged", MeasureKind.Sum), new("compliance", MeasureKind.Ratio, "withinSla", "judged", 100m),
        ],
        Drills = [new("pending", "R19_POD_AGEING", "Proofs still pending", Map(("clock", "=Submission"))), new("rejected", "R20_POD_REJECTIONS", "Rejected proofs", new Dictionary<string, string>())],
        Sorts = Sort("month"),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var pods = await ctx.Facts.Pods();
            foreach (var p in pods)
            {
                var judged = p.Required && p.SubmittedWithinSla is not null;
                d.Rows.Add(new ReportRow
                {
                    ["pod"] = p.PodRef, ["delivery"] = p.DeliveryRef, ["shipment"] = p.ShipmentRef, ["transporter"] = p.TransporterName, ["customer"] = p.Customer, ["month"] = Bucket(p.Date, "month"), ["status"] = p.Status,
                    ["required"] = One(p.Required), ["submitted"] = One(p.Required && p.SubmittedAt is not null), ["pending"] = One(p.Required && p.SubmittedAt is null), ["accepted"] = One(p.Status == "Accepted"),
                    ["rejected"] = One(p.RejectedAt is not null || p.Status == "Rejected"), ["withinSla"] = One(judged && p.SubmittedWithinSla == true), ["judged"] = One(judged),
                });
            }

            d.Cards.Add(await ctx.CardAsync("POD_COMPLIANCE"));
            d.Totals.Add(new("required", "POD required", pods.Count(p => p.Required)));
            d.Totals.Add(new("submitted", "Submitted", pods.Count(p => p.Required && p.SubmittedAt is not null)));
            d.Totals.Add(new("pending", "Pending", pods.Count(p => p.Required && p.SubmittedAt is null), null, "R19_POD_AGEING", Map(("clock", "=Submission"))));
            d.Totals.Add(new("accepted", "Accepted", pods.Count(p => p.Status == "Accepted")));
            d.Totals.Add(new("rejected", "Rejected", pods.Count(p => p.RejectedAt is not null || p.Status == "Rejected"), null, "R20_POD_REJECTIONS", null));
            d.Charts.Add(Chart("status", "Proofs by status", "donut", "status", pods.GroupBy(p => p.Status).Select(g => new ReportRow { ["status"] = g.Key, ["pods"] = g.Count() }), new ChartSeries("pods", "Proofs")));
            d.Notes.Add("Deliveries that need no proof are not applicable and are not counted. A proof still inside its SLA is neither compliant nor late.");
            return d;
        },
    };

    private static readonly string[] Clocks = ["Submission", "Review", "Resubmission"];

    private static ReportSpec PodAgeing() => new()
    {
        Code = "R19_POD_AGEING", Name = "POD ageing", Category = CatPod, Type = ReportType.Analytical, DataSource = "Deliveries (POD)", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Delivery, VendorSafe = true, SortOrder = 190, ExportFormats = "csv,xlsx,pdf",
        Description = "How long proofs have waited, on three separate clocks: from delivery to submission, from submission to review, and from rejection to a corrected submission. Buckets and working days are the organisation's settings.",
        Columns = Cols(
            C("pod", "POD", filter: true), C("delivery", "Delivery"), C("shipment", "Shipment"), C("transporter", "Transporter", filter: true), C("customer", "Customer", filter: true), C("clock", "Ageing clock", FieldType.Status, filter: true),
            C("started", "Clock started", FieldType.DateTime), C("ended", "Clock stopped", FieldType.DateTime), C("ageDays", "Age (days)", FieldType.Whole), C("bucket", "Age bucket", FieldType.Status), C("open", "Still waiting", FieldType.Status),
            C("status", "POD status", FieldType.Status), C("pods", "Proofs", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, "clock", "openOnly", "minAgeDays", FilterNames.Transporter, FilterNames.Customer, FilterNames.Shipment),
        Groupings = Groups(("bucket", "Age bucket"), ("transporter", "Transporter"), ("customer", "Customer"), ("clock", "Ageing clock")),
        Measures = [new("pods", MeasureKind.Count), new("ageDays", MeasureKind.Avg)],
        Drills = [new("pod", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment"))), new("transporter", "R15_POD_COMPLIANCE_BY_TRANSPORTER", "Transporter POD compliance", Map((FilterNames.Transporter, "transporter")))],
        Sorts = Sort("ageDays", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var clock = Clocks.FirstOrDefault(c => c.Equals(ctx.Filters.Get("clock"), StringComparison.OrdinalIgnoreCase)) ?? "Submission";
            var openOnly = !string.Equals(ctx.Filters.Get("openOnly"), "false", StringComparison.OrdinalIgnoreCase);
            var minAge = int.TryParse(ctx.Filters.Get("minAgeDays"), out var m) ? m : 0;
            var pods = await ctx.Facts.Pods(anyDate: openOnly);
            var now = ctx.Now;
            var buckets = AgeBuckets(ctx.Settings.AgeingBuckets);

            IEnumerable<ReportRow> Rows(string which)
            {
                foreach (var p in pods)
                {
                    DateTimeOffset? start;
                    DateTimeOffset? end;
                    bool open;
                    switch (which)
                    {
                        case "Submission":
                            if (!p.Required || p.DeliveryCompletedAt is null) { continue; }
                            start = p.DeliveryCompletedAt; end = p.SubmittedAt; open = p.SubmittedAt is null;
                            break;
                        case "Review":
                            if (p.SubmittedAt is null) { continue; }
                            start = p.SubmittedAt; end = p.ReviewedAt; open = p.ReviewedAt is null && p.Status == "Submitted";
                            break;
                        default:
                            if (p.RejectedAt is null) { continue; }
                            start = p.RejectedAt; end = p.ResubmittedAt; open = p.ResubmittedAt is null && p.Status is "Rejected" or "ResubmissionRequired";
                            break;
                    }

                    var age = AgeDays(start!.Value, end ?? now, ctx.Settings);
                    yield return new ReportRow
                    {
                        ["pod"] = p.PodRef, ["delivery"] = p.DeliveryRef, ["shipment"] = p.ShipmentRef, ["transporter"] = p.TransporterName, ["customer"] = p.Customer, ["clock"] = which, ["started"] = start, ["ended"] = end,
                        ["ageDays"] = age, ["bucket"] = AgeBucket(age, ctx.Settings.AgeingBuckets), ["open"] = open ? "Yes" : "No", ["status"] = p.Status, ["pods"] = 1,
                    };
                }
            }

            foreach (var r in Rows(clock).Where(r => (!openOnly || (string)r["open"]! == "Yes") && (int)r["ageDays"]! >= minAge).OrderByDescending(r => (int)r["ageDays"]!))
            {
                d.Rows.Add(r);
            }

            var all = Clocks.SelectMany(c => Rows(c).Where(r => (string)r["open"]! == "Yes")).ToList();
            d.Charts.Add(Chart("buckets", "Proofs still waiting, by age and clock", "stackedBar", "bucket",
                buckets.Select(b => new ReportRow { ["bucket"] = b, ["Submission"] = all.Count(r => (string)r["clock"]! == "Submission" && (string)r["bucket"]! == b), ["Review"] = all.Count(r => (string)r["clock"]! == "Review" && (string)r["bucket"]! == b), ["Resubmission"] = all.Count(r => (string)r["clock"]! == "Resubmission" && (string)r["bucket"]! == b) }),
                new ChartSeries("Submission", "Awaiting submission"), new ChartSeries("Review", "Awaiting review"), new ChartSeries("Resubmission", "Awaiting resubmission")));
            foreach (var c in Clocks)
            {
                d.Totals.Add(new($"open-{c}", $"Waiting: {c.ToLowerInvariant()}", all.Count(r => (string)r["clock"]! == c), null, null, null, null));
            }

            d.Totals.Add(new("over7", "Waiting more than 7 days", all.Count(r => (int)r["ageDays"]! > 7), null, null, null, all.Any(r => (int)r["ageDays"]! > 7) ? "warn" : null));
            d.Notes.Add($"Showing the {clock.ToLowerInvariant()} clock{(openOnly ? ", items still waiting" : string.Empty)}. Ages count {(ctx.Settings.AgeingUsesWorkingDays ? "working days" : "calendar days")}.");
            return d;
        },
    };

    private static ReportSpec Rejections() => new()
    {
        Code = "R20_POD_REJECTIONS", Name = "POD rejection report", Category = CatPod, Type = ReportType.Operational, DataSource = "Deliveries (POD)", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Delivery, VendorSafe = true, SortOrder = 200,
        Description = "Proofs that were rejected: why, by whom, when, whether a corrected proof has come back, and how long it has been.",
        Columns = Cols(
            C("pod", "POD", filter: true), C("shipment", "Shipment"), C("transporter", "Transporter", filter: true), C("customer", "Customer", filter: true), C("reason", "Rejection reason", filter: true), C("rejectedBy", "Rejected by"),
            C("rejectedAt", "Rejected at", FieldType.DateTime), C("resubmission", "Resubmission", FieldType.Status), C("ageDays", "Age (days)", FieldType.Whole), C("count", "Rejections", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Customer, FilterNames.Shipment),
        Groupings = Groups(("reason", "Reason"), ("transporter", "Transporter"), ("customer", "Customer")),
        Measures = [new("count", MeasureKind.Count), new("ageDays", MeasureKind.Avg)],
        Drills = [new("pod", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("rejectedAt", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rejected = (await ctx.Facts.Pods()).Where(p => p.RejectedAt is not null).ToList();
            foreach (var p in rejected)
            {
                d.Rows.Add(new ReportRow
                {
                    ["pod"] = p.PodRef, ["shipment"] = p.ShipmentRef, ["transporter"] = p.TransporterName, ["customer"] = p.Customer, ["reason"] = p.RejectionReason ?? "Not recorded", ["rejectedBy"] = p.RejectedBy, ["rejectedAt"] = p.RejectedAt,
                    ["resubmission"] = p.ResubmittedAt is not null ? "Resubmitted" : "Awaiting resubmission", ["ageDays"] = AgeDays(p.RejectedAt!.Value, p.ResubmittedAt ?? ctx.Now, ctx.Settings), ["count"] = 1,
                });
            }

            d.Totals.Add(new("rejected", "Rejected proofs", rejected.Count));
            d.Totals.Add(new("awaiting", "Awaiting resubmission", rejected.Count(p => p.ResubmittedAt is null), null, "R19_POD_AGEING", Map(("clock", "=Resubmission"))));
            d.Charts.Add(Chart("reasons", "Why proofs were rejected", "bar", "reason",
                rejected.GroupBy(p => p.RejectionReason ?? "Not recorded").OrderByDescending(g => g.Count()).Take(ctx.Settings.TopN).Select(g => new ReportRow { ["reason"] = g.Key, ["rejections"] = g.Count() }), new ChartSeries("rejections", "Rejections")));
            return d;
        },
    };

    private static ReportSpec Discrepancies(string code, string name, string type, int order, string tag)
    {
        var shortage = type == "Shortage";
        return new ReportSpec
        {
            Code = code, Name = name, Category = CatPod, Type = ReportType.Operational, DataSource = "Deliveries (discrepancies)", Refresh = RefreshType.NearRealTime,
            Permission = ReportingPermissions.Delivery, SortOrder = order, ExportFormats = "csv,xlsx,pdf", Comparison = true,
            Description = shortage ? "Quantities that arrived short: ordered, dispatched, delivered, shortage %, reason, claim and value." : "Goods that arrived damaged: quantity, damage type, damage %, evidence, claim and value.",
            Columns = Cols(
                C("shipment", "Shipment", filter: true), C("customer", "Customer", filter: true), C("transporter", "Transporter", filter: true), C("sku", "SKU", filter: true), C("ordered", "Ordered", FieldType.Number), C("dispatched", "Dispatched", FieldType.Number),
                C("delivered", "Delivered", FieldType.Number), C("quantity", shortage ? "Short" : "Damaged qty", FieldType.Number), C("pct", shortage ? "Shortage %" : "Damage %", FieldType.Percent), C("reason", "Reason", filter: true),
                C("damageType", "Damage type", filter: true, visible: !shortage), C("evidence", "Evidence", FieldType.Status, visible: !shortage), C("claim", "Claim"), C("value", "Value", FieldType.Currency), C("date", "Date", FieldType.Date), C("delivery", "Delivery"), C("lines", "Lines", FieldType.Whole)),
            Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Customer, FilterNames.Shipment),
            Groupings = Groups(("transporter", "Transporter"), ("customer", "Customer"), ("sku", "SKU"), ("reason", "Reason")),
            Measures = [new("lines", MeasureKind.Count), new("quantity", MeasureKind.Sum), new("dispatched", MeasureKind.Sum), new("value", MeasureKind.Sum), new("pct", MeasureKind.Ratio, "quantity", "dispatched", 100m)],
            Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment")))],
            Sorts = Sort("value", true),
            Build = async (ctx, ct) =>
            {
                var d = new ReportData();
                var rows = (await ctx.Facts.Discrepancies()).Where(x => x.Type == type).ToList();
                foreach (var x in rows)
                {
                    d.Rows.Add(new ReportRow
                    {
                        ["shipment"] = x.ShipmentRef, ["customer"] = x.Customer, ["transporter"] = x.TransporterName, ["sku"] = x.Sku, ["ordered"] = x.Ordered, ["dispatched"] = x.Dispatched, ["delivered"] = x.Delivered, ["quantity"] = x.Quantity,
                        ["pct"] = Pct(x.Quantity, x.Dispatched), ["reason"] = x.Reason ?? "Not recorded", ["damageType"] = x.DamageType, ["evidence"] = x.HasEvidence ? "Yes" : "No", ["claim"] = x.ClaimRef, ["value"] = x.Value, ["date"] = x.Date,
                        ["delivery"] = x.DeliveryRef, ["lines"] = 1,
                    });
                }

                d.Cards.Add(await ctx.CardAsync(shortage ? "SHORTAGE_PCT" : "DAMAGE_PCT"));
                d.Cards.Add(await ctx.CardAsync("CLAIMS_RATE"));
                d.Totals.Add(new("lines", shortage ? "Shortage lines" : "Damage lines", rows.Count));
                d.Totals.Add(new("qty", shortage ? "Units short" : "Units damaged", rows.Sum(r => r.Quantity), "number"));
                d.Totals.Add(new("value", "Value", rows.Where(r => r.Value is not null).Sum(r => r.Value!.Value), "currency"));
                d.Totals.Add(new("claims", "With a claim", rows.Count(r => r.ClaimRef is not null)));
                d.Charts.Add(Chart("by-transporter", $"{(shortage ? "Units short" : "Units damaged")} by transporter", "bar", "transporter",
                    rows.Where(r => r.TransporterName is not null).GroupBy(r => r.TransporterName!).OrderByDescending(g => g.Sum(r => r.Quantity)).Take(ctx.Settings.TopN).Select(g => new ReportRow { ["transporter"] = g.Key, ["quantity"] = g.Sum(r => r.Quantity) }), new ChartSeries("quantity", shortage ? "Short" : "Damaged")));
                _ = tag;
                return d;
            },
        };
    }

    private static ReportSpec FailedRefused() => new()
    {
        Code = "R23_FAILED_REFUSED", Name = "Failed / refused deliveries", Category = CatPod, Type = ReportType.Operational, DataSource = "Deliveries", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Delivery, VendorSafe = true, SortOrder = 230,
        Description = "Deliveries that failed or were refused: the attempt, the reason, where, the current status and what happens next.",
        Columns = Cols(
            C("delivery", "Delivery", filter: true), C("shipment", "Shipment"), C("customer", "Customer", filter: true), C("transporter", "Transporter", filter: true), C("attempts", "Attempts", FieldType.Whole), C("outcome", "Outcome", FieldType.Status, filter: true),
            C("reason", "Failure / refusal reason", filter: true), C("date", "Date", FieldType.Date), C("location", "Location"), C("lane", "Lane"), C("nextAction", "Next action"), C("count", "Deliveries", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Transporter, FilterNames.Customer, FilterNames.Lane, FilterNames.Status).Select(f => f.Name == FilterNames.Status ? f.With("deliveryStatus") : f).ToList(),
        Groupings = Groups(("reason", "Reason"), ("transporter", "Transporter"), ("customer", "Customer"), ("outcome", "Outcome")),
        Measures = [new("count", MeasureKind.Count)],
        Drills = [new("delivery", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment")))],
        Sorts = Sort("date", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rows = (await ctx.Facts.Deliveries()).Where(x => x.Status is "Failed" or "Refused").ToList();
            foreach (var x in rows)
            {
                d.Rows.Add(new ReportRow
                {
                    ["delivery"] = x.DeliveryRef, ["shipment"] = x.ShipmentRef, ["customer"] = x.Customer, ["transporter"] = x.TransporterName, ["attempts"] = x.Attempts, ["outcome"] = x.Status, ["reason"] = x.FailureReason ?? "Not recorded",
                    ["date"] = x.Date, ["location"] = x.Location, ["lane"] = x.Lane, ["nextAction"] = x.NextAction, ["count"] = 1,
                });
            }

            d.Totals.Add(new("failed", "Failed", rows.Count(x => x.Status == "Failed")));
            d.Totals.Add(new("refused", "Refused", rows.Count(x => x.Status == "Refused")));
            d.Charts.Add(Chart("reasons", "Reasons", "donut", "reason", rows.GroupBy(x => x.FailureReason ?? "Not recorded").Select(g => new ReportRow { ["reason"] = g.Key, ["deliveries"] = g.Count() }), new ChartSeries("deliveries", "Deliveries")));
            return d;
        },
    };
}
