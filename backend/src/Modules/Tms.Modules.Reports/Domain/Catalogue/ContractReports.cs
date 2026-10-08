using Tms.SharedKernel.Contracts;
using static Tms.Modules.Reports.Domain.Kit;

namespace Tms.Modules.Reports.Domain;

/// <summary>R31–R35: freight contracts. Commercial data: only people with the contracts report permission see these. Rates and ratings are shown as they were when made.</summary>
internal static class ContractReports
{
    public static IEnumerable<ReportSpec> All()
    {
        yield return Expiry();
        yield return Coverage();
        yield return RateSlabs();
        yield return Dph();
        yield return RatingAudit();
    }

    private static string Band(int? days, int[] bands)
    {
        if (days is null)
        {
            return "No end date";
        }

        if (days < 0)
        {
            return "Expired";
        }

        foreach (var b in bands.Order())
        {
            if (days <= b)
            {
                return $"Within {b} days";
            }
        }

        return $"More than {bands.Max()} days";
    }

    private static ReportSpec Expiry() => new()
    {
        Code = "R31_CONTRACT_EXPIRY", Name = "Contract status & expiry", Category = CatContracts, Type = ReportType.Operational, DataSource = "Freight Contracts", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Contracts, SortOrder = 310, ExportFormats = "csv,xlsx,pdf",
        Description = "Every contract with its version, validity, status, days to expiry and renewal status, so renewals are started in time.",
        Columns = Cols(
            C("contract", "Contract", filter: true), C("transporter", "Transporter", filter: true), C("type", "Type", filter: true), C("version", "Version", FieldType.Whole), C("start", "Start", FieldType.Date), C("end", "End", FieldType.Date),
            C("status", "Status", FieldType.Status, filter: true), C("daysToExpiry", "Days to expiry", FieldType.Whole), C("band", "Expiry band", FieldType.Status), C("renewalStatus", "Renewal status", FieldType.Status), C("rates", "Rates", FieldType.Whole), C("count", "Contracts", FieldType.Whole)),
        Filters = Filters(FilterNames.Transporter, FilterNames.Contract, FilterNames.ServiceType, FilterNames.Status).Select(f => f.Name == FilterNames.Status ? f.With("contractStatus") : f).ToList(),
        Groupings = Groups(("band", "Expiry band"), ("status", "Status"), ("transporter", "Transporter"), ("type", "Type")),
        Measures = [new("count", MeasureKind.Count), new("rates", MeasureKind.Sum)],
        Drills = [new("contract", "R33_RATE_SLAB", "Rates of this contract", Map((FilterNames.Contract, "contract")))],
        Sorts = Sort("daysToExpiry"),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var contracts = await ctx.Facts.Contracts();
            foreach (var c in contracts)
            {
                int? days = c.End == DateOnly.MaxValue ? null : c.End.DayNumber - ctx.Today.DayNumber;
                d.Rows.Add(new ReportRow
                {
                    ["contract"] = c.ContractRef, ["transporter"] = c.TransporterName, ["type"] = c.Type, ["version"] = c.Version, ["start"] = c.Start, ["end"] = c.End, ["status"] = c.Status, ["daysToExpiry"] = days,
                    ["band"] = c.Status is "Active" ? Band(days, ctx.Settings.ExpiryBands) : c.Status, ["renewalStatus"] = c.RenewalStatus, ["rates"] = c.Rates, ["count"] = 1,
                });
            }

            var active = contracts.Where(c => c.Status == "Active").ToList();
            d.Totals.Add(new("contracts", "Contracts", contracts.Count));
            d.Totals.Add(new("active", "Active", active.Count));
            foreach (var b in ctx.Settings.ExpiryBands.Order())
            {
                var n = active.Count(c => c.End.DayNumber - ctx.Today.DayNumber is { } x && x >= 0 && x <= b);
                d.Totals.Add(new($"within-{b}", $"Expiring within {b} days", n, null, null, null, n > 0 && b <= 30 ? "warn" : null));
            }

            var bandOrder = ctx.Settings.ExpiryBands.Order().Select(b => $"Within {b} days").Append($"More than {ctx.Settings.ExpiryBands.Max()} days").Prepend("Expired").ToList();
            d.Charts.Add(Chart("bands", "Active contracts by time to expiry", "bar", "band", bandOrder.Select(b => new ReportRow { ["band"] = b, ["contracts"] = d.Rows.Count(r => (string)r["band"]! == b) }), new ChartSeries("contracts", "Contracts")));
            return d;
        },
    };

    private static ReportSpec Coverage() => new()
    {
        Code = "R32_RATE_COVERAGE", Name = "Rate coverage", Category = CatContracts, Type = ReportType.Analytical, DataSource = "Freight Contracts + Shipments", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Contracts, SortOrder = 320, ExportFormats = "csv,xlsx,pdf",
        Description = "Lanes with real loads against the rates that cover them: lane rates, zone rates, fallback cover and the loads that have no applicable rate at all.",
        Columns = Cols(
            C("lane", "Lane", filter: true), C("region", "Region", filter: true), C("service", "Service", filter: true), C("loads", "Loads", FieldType.Whole), C("coverType", "Covered by", FieldType.Status), C("laneRates", "Lane rates", FieldType.Whole),
            C("zoneRates", "Zone rates", FieldType.Whole), C("loadsWithoutRate", "Loads without a rate", FieldType.Whole), C("lanes", "Lanes", FieldType.Whole), C("uncovered", "Uncovered lanes", FieldType.Whole)),
        Filters = Filters(FilterNames.Lane, FilterNames.Region, FilterNames.ServiceType, FilterNames.Origin, FilterNames.Destination),
        Groupings = Groups(("coverType", "Covered by"), ("region", "Region"), ("service", "Service")),
        Measures = [new("lanes", MeasureKind.Count), new("loads", MeasureKind.Sum), new("laneRates", MeasureKind.Sum), new("zoneRates", MeasureKind.Sum), new("loadsWithoutRate", MeasureKind.Sum), new("uncovered", MeasureKind.Sum)],
        Drills = [new("lane", "R37_LANE_PERFORMANCE", "Lane performance", Map((FilterNames.Lane, "lane"))), new("laneRates", "R33_RATE_SLAB", "Rates on this lane", Map((FilterNames.Lane, "lane")))],
        Sorts = Sort("loadsWithoutRate", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rows = await ctx.Facts.Coverage();
            foreach (var c in rows)
            {
                d.Rows.Add(new ReportRow
                {
                    ["lane"] = c.Lane, ["region"] = ctx.Settings.RegionOf(c.Origin, null), ["service"] = c.Service, ["loads"] = c.Loads, ["coverType"] = c.CoverType, ["laneRates"] = c.LaneRates, ["zoneRates"] = c.ZoneRates,
                    ["loadsWithoutRate"] = c.LoadsWithoutRate, ["lanes"] = 1, ["uncovered"] = One(c.CoverType == "None"),
                });
            }

            d.Cards.Add(await ctx.CardAsync("LOADS_WITHOUT_RATE"));
            d.Totals.Add(new("required", "Required lanes", rows.Count));
            d.Totals.Add(new("covered", "Covered lanes", rows.Count(c => c.CoverType != "None")));
            d.Totals.Add(new("uncovered", "Uncovered lanes", rows.Count(c => c.CoverType == "None"), null, null, null, rows.Any(c => c.CoverType == "None") ? "bad" : null));
            d.Totals.Add(new("laneRates", "Lane rates", rows.Sum(c => c.LaneRates)));
            d.Totals.Add(new("zoneRates", "Zone rates", rows.Sum(c => c.ZoneRates)));
            d.Totals.Add(new("fallback", "Covered only by fallback", rows.Count(c => c.CoverType == "Fallback")));
            d.Charts.Add(Chart("cover", "Lanes by how they are covered", "donut", "coverType", rows.GroupBy(c => c.CoverType).Select(g => new ReportRow { ["coverType"] = g.Key, ["lanes"] = g.Count() }), new ChartSeries("lanes", "Lanes")));
            return d;
        },
    };

    private static ReportSpec RateSlabs() => new()
    {
        Code = "R33_RATE_SLAB", Name = "Rate / slab report", Category = CatContracts, Type = ReportType.Operational, DataSource = "Freight Contracts", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Contracts, SortOrder = 330, ExportFormats = "csv,xlsx,pdf",
        Description = "Every rate with its lane or zone, vehicle, service, weight, distance and volume slabs, rate, minimum, maximum and validity.",
        Columns = Cols(
            C("contract", "Contract", filter: true), C("transporter", "Transporter", filter: true), C("rateCode", "Rate code"), C("rateVersion", "Rate version", FieldType.Whole), C("lane", "Lane", filter: true), C("origin", "Origin"), C("destination", "Destination"),
            C("zone", "Zone", filter: true), C("vehicleType", "Vehicle", filter: true), C("service", "Service", filter: true), C("weightSlab", "Weight slab"), C("distanceSlab", "Distance slab"), C("volumeSlab", "Volume slab"),
            C("rate", "Rate", FieldType.Currency), C("rateBasis", "Basis"), C("minimum", "Minimum", FieldType.Currency), C("maximum", "Maximum", FieldType.Currency), C("validFrom", "Valid from", FieldType.Date), C("validTo", "Valid to", FieldType.Date), C("rates", "Rates", FieldType.Whole)),
        Filters = Filters(FilterNames.Contract, FilterNames.Transporter, FilterNames.Lane, FilterNames.Origin, FilterNames.Destination, FilterNames.Zone, FilterNames.VehicleType, FilterNames.ServiceType),
        Groupings = Groups(("transporter", "Transporter"), ("contract", "Contract"), ("service", "Service"), ("vehicleType", "Vehicle")),
        Measures = [new("rates", MeasureKind.Count), new("rate", MeasureKind.Avg), new("minimum", MeasureKind.Min), new("maximum", MeasureKind.Max)],
        Drills = [new("contract", "R31_CONTRACT_EXPIRY", "Contract status", Map((FilterNames.Contract, "contract"))), new("lane", "R37_LANE_PERFORMANCE", "Lane performance", Map((FilterNames.Lane, "lane")))],
        Sorts = Sort("contract"),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rates = await ctx.Facts.Rates();
            foreach (var r in rates)
            {
                d.Rows.Add(new ReportRow
                {
                    ["contract"] = r.ContractRef, ["transporter"] = r.TransporterName, ["rateCode"] = r.RateCode, ["rateVersion"] = r.RateVersion, ["lane"] = FactDims.LaneOf(r.Origin, r.Destination), ["origin"] = r.Origin, ["destination"] = r.Destination,
                    ["zone"] = r.Zone, ["vehicleType"] = r.VehicleType, ["service"] = r.Service, ["weightSlab"] = r.WeightSlab, ["distanceSlab"] = r.DistanceSlab, ["volumeSlab"] = r.VolumeSlab, ["rate"] = r.Rate, ["rateBasis"] = r.RateBasis,
                    ["minimum"] = r.Minimum, ["maximum"] = r.Maximum, ["validFrom"] = r.ValidFrom, ["validTo"] = r.ValidTo, ["rates"] = 1,
                });
            }

            d.Totals.Add(new("rates", "Rates", rates.Count));
            d.Totals.Add(new("contracts", "Contracts", rates.Select(r => r.ContractRef).Distinct().Count()));
            d.Notes.Add("Rates are shown as held in their contract version. Averaging rates of different bases (per trip, per kg) is not meaningful: group by service and basis before reading the average.");
            return d;
        },
    };

    private static ReportSpec Dph() => new()
    {
        Code = "R34_DPH_SURCHARGE", Name = "DPH & surcharge report", Category = CatContracts, Type = ReportType.Operational, DataSource = "Freight Contracts", Refresh = RefreshType.Scheduled,
        Permission = ReportingPermissions.Contracts, SortOrder = 340, ExportFormats = "csv,xlsx,pdf",
        Description = "Diesel price escalation: base and current diesel price, the variation, the fuel share of the freight, the adjustment and the extra charges on each contract.",
        Columns = Cols(
            C("contract", "Contract", filter: true), C("transporter", "Transporter", filter: true), C("baseDiesel", "Base diesel price", FieldType.Currency), C("currentDiesel", "Current diesel price", FieldType.Currency),
            C("variationPct", "Diesel variation %", FieldType.Percent), C("fuelComponentPct", "Fuel component %", FieldType.Percent), C("adjustmentPct", "DPH adjustment %", FieldType.Percent), C("effectiveDate", "Effective date", FieldType.Date), C("accessorials", "Accessorials")),
        Filters = Filters(FilterNames.Contract, FilterNames.Transporter),
        Drills = [new("contract", "R33_RATE_SLAB", "Rates of this contract", Map((FilterNames.Contract, "contract")))],
        Sorts = Sort("adjustmentPct", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rows = await ctx.Facts.Dph();
            foreach (var x in rows)
            {
                d.Rows.Add(new ReportRow
                {
                    ["contract"] = x.ContractRef, ["transporter"] = x.TransporterName, ["baseDiesel"] = x.BaseDieselPrice, ["currentDiesel"] = x.CurrentDieselPrice, ["variationPct"] = x.VariationPct, ["fuelComponentPct"] = x.FuelComponentPct,
                    ["adjustmentPct"] = x.AdjustmentPct, ["effectiveDate"] = x.EffectiveDate, ["accessorials"] = x.Accessorials,
                });
            }

            d.Totals.Add(new("contracts", "Contracts with a diesel clause", rows.Count));
            d.Totals.Add(new("avg", "Average adjustment %", rows.Count == 0 ? null : Math.Round(rows.Average(r => r.AdjustmentPct), 2), "percent"));
            return d;
        },
    };

    private static ReportSpec RatingAudit() => new()
    {
        Code = "R35_FREIGHT_RATING_AUDIT", Name = "Freight rating audit", Category = CatContracts, Type = ReportType.Operational, DataSource = "Freight Contracts (kept ratings)", Refresh = RefreshType.NearRealTime,
        Permission = ReportingPermissions.Contracts, SortOrder = 350, ExportFormats = "csv,xlsx,pdf",
        Description = "For every rated shipment: the contract and rate version used, the slab, base freight, diesel adjustment, extra charges, final freight and the calculation version, exactly as kept when the rating was made.",
        Columns = Cols(
            C("shipment", "Shipment", filter: true), C("transporter", "Transporter", filter: true), C("contract", "Contract", filter: true), C("contractVersion", "Contract version", FieldType.Whole), C("rateCode", "Rate"), C("rateVersion", "Rate version", FieldType.Whole),
            C("lane", "Lane", filter: true), C("weightKg", "Weight (kg)", FieldType.Number), C("distanceKm", "Distance (km)", FieldType.Number), C("volumeCbm", "Volume (CBM)", FieldType.Number), C("selectedSlab", "Selected slab"),
            C("baseFreight", "Base freight", FieldType.Currency), C("dph", "DPH", FieldType.Currency), C("accessorials", "Accessorials", FieldType.Currency), C("discount", "Discount", FieldType.Currency), C("finalFreight", "Final freight", FieldType.Currency),
            C("calculationVersion", "Calculation version"), C("ratedAt", "Rated at", FieldType.DateTime), C("ratings", "Ratings", FieldType.Whole)),
        Filters = Filters(FilterNames.FromDate, FilterNames.ToDate, FilterNames.Shipment, FilterNames.Transporter, FilterNames.Contract, FilterNames.Lane, FilterNames.Region, FilterNames.ServiceType),
        Groupings = Groups(("transporter", "Transporter"), ("contract", "Contract"), ("lane", "Lane")),
        Measures = [new("ratings", MeasureKind.Count), new("baseFreight", MeasureKind.Sum), new("dph", MeasureKind.Sum), new("accessorials", MeasureKind.Sum), new("discount", MeasureKind.Sum), new("finalFreight", MeasureKind.Sum)],
        Drills = [new("shipment", "R36_SHIPMENT_360", "Open Shipment 360", Map((FilterNames.Shipment, "shipment"))), new("contract", "R33_RATE_SLAB", "Rates of this contract", Map((FilterNames.Contract, "contract")))],
        Sorts = Sort("ratedAt", true),
        Build = async (ctx, ct) =>
        {
            var d = new ReportData();
            var rows = await ctx.Facts.Ratings();
            foreach (var x in rows)
            {
                d.Rows.Add(new ReportRow
                {
                    ["shipment"] = x.ShipmentRef, ["transporter"] = x.TransporterName, ["contract"] = x.ContractRef, ["contractVersion"] = x.ContractVersion, ["rateCode"] = x.RateCode, ["rateVersion"] = x.RateVersion, ["lane"] = x.Lane,
                    ["weightKg"] = x.WeightKg, ["distanceKm"] = x.DistanceKm, ["volumeCbm"] = x.VolumeCbm, ["selectedSlab"] = x.SelectedSlab, ["baseFreight"] = x.BaseFreight, ["dph"] = x.Dph, ["accessorials"] = x.Accessorials,
                    ["discount"] = x.Discount, ["finalFreight"] = x.FinalFreight, ["calculationVersion"] = x.CalculationVersion, ["ratedAt"] = x.RatedAt, ["ratings"] = 1,
                });
            }

            d.Cards.Add(await ctx.CardAsync("FREIGHT_SPEND"));
            d.Totals.Add(new("ratings", "Rated shipments", rows.Count));
            d.Totals.Add(new("base", "Base freight", rows.Sum(r => r.BaseFreight), "currency"));
            d.Totals.Add(new("dph", "DPH", rows.Sum(r => r.Dph), "currency"));
            d.Totals.Add(new("acc", "Accessorials", rows.Sum(r => r.Accessorials), "currency"));
            d.Totals.Add(new("final", "Final freight", rows.Sum(r => r.FinalFreight), "currency"));
            d.Notes.Add("Every line is the rating as it was kept: the contract, rate and calculation versions and the diesel snapshot of that day. Later changes to contracts, rates or diesel prices never alter it.");
            return d;
        },
    };
}
