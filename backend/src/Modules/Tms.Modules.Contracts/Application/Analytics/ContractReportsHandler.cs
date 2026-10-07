using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Tms.Modules.Contracts.Application.Import;
using Tms.Modules.Contracts.Application.Rates;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Application.Analytics;

/// <summary>The contract reports as Excel or CSV: contracts, rates, DPH, extra charges, expiry, coverage, validation, capacity, rating history, usage and comparisons.</summary>
internal sealed class ContractReportsHandler(
    ContractsDbContext db, ContractAccess access, DashboardHandler dashboard, Terms.DphHandler dph, ITransporterDirectory transporters, IVehicleTypeDirectory vehicleTypes, TimeProvider clock)
{
    public static IReadOnlyList<string> Reports { get; } =
    [
        "contracts", "active-contracts", "contract-expiry", "rates", "rate-expiry", "rate-coverage", "dph", "dph-revisions", "accessorials", "rate-validation", "capacity", "rating-history",
        "rate-usage", "lane-comparison", "transporter-comparison",
    ];

    public async Task<Result<SheetFile>> RunAsync(string report, string? format, DateOnly? from, DateOnly? to, Guid? transporterId, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ContractAccess.Forbidden;
        }

        var name = report.ToLowerInvariant();
        if (!Reports.Contains(name))
        {
            return Error.NotFound("reports.not_found", "There is no such report.");
        }

        var today = clock.TodayInIndia();
        var rows = await BuildAsync(name, from, to, transporterId, today, cancellationToken);
        if (rows.Count == 0)
        {
            rows.Add(new Dictionary<string, object?> { ["Result"] = "Nothing to report for this selection." });
        }

        return await Sheets.WriteAsync(format ?? "xlsx", $"contracts-{name}-{today:yyyyMMdd}", rows);
    }

    private async Task<List<Dictionary<string, object?>>> BuildAsync(string report, DateOnly? from, DateOnly? to, Guid? transporterId, DateOnly today, CancellationToken ct)
    {
        var contracts = db.Contracts.AsNoTracking().AsQueryable();
        if (transporterId is { } t)
        {
            contracts = contracts.Where(c => c.TransporterId == t);
        }

        switch (report)
        {
            case "contracts" or "active-contracts":
            {
                var list = await (report == "active-contracts" ? contracts.Where(c => c.Status == ContractStatus.Active && c.EffectiveFrom <= today && c.EffectiveTo >= today) : contracts).OrderBy(c => c.Number).ThenBy(c => c.Revision).Take(20_000).ToListAsync(ct);
                var names = await transporters.GetAsync(list.Select(c => c.TransporterId), ct);
                return list.Select(c => new Dictionary<string, object?>
                {
                    ["Contract"] = c.Number, ["Version"] = c.Revision, ["Kind"] = c.RevisionKind.ToString(), ["Transporter"] = names.TryGetValue(c.TransporterId, out var n) ? n.LegalName : "Unknown", ["Title"] = c.Title,
                    ["Services"] = string.Join("/", c.EffectiveServices), ["Status"] = c.EffectiveStatus(today).ToString(), ["From"] = c.EffectiveFrom, ["To"] = c.EffectiveTo, ["Currency"] = c.Currency, ["Rates"] = c.RateCount,
                    ["Estimated annual spend"] = c.EstimatedAnnualSpend, ["Renewal notice days"] = c.RenewalNoticeDays, ["Auto renewal"] = c.AutoRenewal ? "Yes" : "No",
                }).ToList();
            }

            case "contract-expiry" or "rate-expiry":
            {
                var expiry = await dashboard.ExpiryAsync(null, ct);
                return expiry.IsFailure ? [] : expiry.Value.Items.Where(i => report == "contract-expiry" ? i.Kind is "Contract" or "Document" or "Capacity commitment" : i.Kind is "Rate" or "DPH rule")
                    .Select(i => new Dictionary<string, object?> { ["Kind"] = i.Kind, ["Reference"] = i.Reference, ["Title"] = i.Title, ["Contract"] = i.ContractNumber, ["Expires on"] = i.ExpiresOn, ["Days left"] = i.DaysLeft, ["Band"] = i.Band }).ToList();
            }

            case "rates":
            {
                var list = await db.RateCards.AsNoTracking().Join(contracts, r => r.ContractId, c => c.Id, (r, c) => new { Card = r, Contract = c }).OrderBy(x => x.Contract.Number).ThenBy(x => x.Card.Code).Take(50_000).ToListAsync(ct);
                var names = await transporters.GetAsync(list.Select(x => x.Contract.TransporterId), ct);
                var types = await vehicleTypes.GetAsync(list.Where(x => x.Card.VehicleTypeId.HasValue).Select(x => x.Card.VehicleTypeId!.Value), ct);
                return list.SelectMany(x => RateSheet.ToRows(x.Contract, x.Card, names.TryGetValue(x.Contract.TransporterId, out var n) ? n.LegalName : "Unknown", x.Card.VehicleTypeId is { } v && types.TryGetValue(v, out var vt) ? vt.Name : null)).ToList();
            }

            case "rate-coverage":
            {
                var coverage = await dashboard.CoverageAsync(ct);
                if (coverage.IsFailure)
                {
                    return [];
                }

                var rows = coverage.Value.Uncovered.Select(l => new Dictionary<string, object?> { ["Lane"] = l.Lane, ["Service"] = l.Service, ["State"] = l.State, ["Requests"] = l.Requests, ["Failed"] = l.Failed, ["Last reason"] = l.Reason }).ToList();
                rows.Insert(0, new Dictionary<string, object?> { ["Lane"] = "SUMMARY", ["Service"] = $"{coverage.Value.RequiredLanes} lanes asked for", ["State"] = $"{coverage.Value.CoveredLanes} covered, {coverage.Value.UncoveredLanes} uncovered", ["Requests"] = coverage.Value.LoadsWithoutRate, ["Failed"] = null, ["Last reason"] = "loads without a rate, last 30 days" });
                return rows;
            }

            case "dph" or "dph-revisions":
            {
                var overview = await dph.ListAsync(null, report == "dph-revisions" ? true : null, ct);
                return overview.IsFailure ? [] : overview.Value.Where(o => report == "dph" || o.RevisionDue).Select(o => new Dictionary<string, object?>
                {
                    ["Contract"] = o.Rule.ContractNumber, ["Contract version"] = o.Rule.ContractRevision, ["Rule"] = o.Rule.Code, ["Rule version"] = o.Rule.Version, ["Formula"] = o.Rule.Spec.Formula.ToString(), ["Region"] = o.Rule.Spec.Region,
                    ["Base diesel"] = o.Rule.Spec.BaseDieselPrice, ["Current diesel"] = o.CurrentPrice, ["Variation %"] = o.VariationPercent, ["Fuel share %"] = o.Rule.Spec.FuelComponentPercent, ["Threshold %"] = o.Rule.Spec.ThresholdPercent,
                    ["Adjustment %"] = o.AdjustmentPercent, ["From"] = o.Rule.EffectiveFrom, ["To"] = o.Rule.EffectiveTo, ["In force"] = o.Rule.InForce ? "Yes" : "No", ["Revision due"] = o.RevisionDue ? "Yes" : "No",
                }).ToList();
            }

            case "accessorials":
            {
                var list = await db.ContractAccessorials.AsNoTracking().Join(contracts, a => a.ContractId, c => c.Id, (a, c) => new { Accessorial = a, Contract = c }).OrderBy(x => x.Contract.Number).Take(20_000).ToListAsync(ct);
                return list.Select(x => new Dictionary<string, object?>
                {
                    ["Contract"] = x.Contract.Number, ["Version"] = x.Contract.Revision, ["Charge"] = x.Accessorial.Code, ["Name"] = x.Accessorial.Spec.Name, ["Calculation"] = x.Accessorial.Spec.Calc.ToString(), ["Unit"] = x.Accessorial.Spec.Unit,
                    ["Rate"] = x.Accessorial.Spec.Rate, ["Included"] = x.Accessorial.Spec.IncludedQuantity, ["Minimum"] = x.Accessorial.Spec.MinimumCharge, ["Maximum"] = x.Accessorial.Spec.MaximumCharge,
                    ["From"] = x.Accessorial.Spec.ValidFrom, ["To"] = x.Accessorial.Spec.ValidTo,
                }).ToList();
            }

            case "rate-validation":
            {
                var overview = await dashboard.ValidationAsync(ct);
                return overview.IsFailure ? [] : overview.Value.Contracts.SelectMany(c => c.Issues.Select(i => new Dictionary<string, object?>
                    { ["Contract"] = c.Reference, ["Status"] = c.Status.ToString(), ["Severity"] = i.Severity, ["Rate"] = i.Row, ["Field"] = i.Field, ["Code"] = i.Code, ["Message"] = i.Message })).ToList();
            }

            case "capacity":
            {
                var list = await db.Capacities.AsNoTracking().Join(contracts.Where(c => c.Status == ContractStatus.Active || c.Status == ContractStatus.Suspended), a => a.ContractId, c => c.Id, (a, c) => new { Cap = a, Contract = c }).Take(20_000).ToListAsync(ct);
                var names = await transporters.GetAsync(list.Select(x => x.Contract.TransporterId), ct);
                var types = await vehicleTypes.GetAsync(list.Where(x => x.Cap.Spec.VehicleTypeId.HasValue).Select(x => x.Cap.Spec.VehicleTypeId!.Value), ct);
                return list.Select(x => new Dictionary<string, object?>
                {
                    ["Contract"] = x.Contract.Reference, ["Transporter"] = names.TryGetValue(x.Contract.TransporterId, out var n) ? n.LegalName : "Unknown",
                    ["Vehicle type"] = x.Cap.Spec.VehicleTypeId is { } v && types.TryGetValue(v, out var vt) ? vt.Name : null, ["Committed vehicles"] = x.Cap.Spec.CommittedVehicleCount, ["Committed capacity kg"] = x.Cap.Spec.CommittedCapacityKg,
                    ["Minimum monthly trips"] = x.Cap.Spec.MinimumMonthlyTrips, ["Minimum monthly tonnage"] = x.Cap.Spec.MinimumMonthlyTonnage, ["Target share %"] = x.Cap.Spec.TargetBusinessSharePct, ["From"] = x.Cap.Spec.ValidFrom, ["To"] = x.Cap.Spec.ValidTo,
                }).ToList();
            }

            case "rating-history":
            {
                var ratings = db.Ratings.AsNoTracking().AsQueryable();
                if (transporterId is { } rt)
                {
                    ratings = ratings.Where(r => r.TransporterId == rt);
                }

                if (from is { } f)
                {
                    var since = new DateTimeOffset(f.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
                    ratings = ratings.Where(r => r.CalculatedAt >= since);
                }

                if (to is { } e)
                {
                    var until = new DateTimeOffset(e.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
                    ratings = ratings.Where(r => r.CalculatedAt < until);
                }

                var list = await ratings.OrderByDescending(r => r.CalculatedAt).Take(50_000).ToListAsync(ct);
                var names = await transporters.GetAsync(list.Where(r => r.TransporterId.HasValue).Select(r => r.TransporterId!.Value), ct);
                return list.Select(r => new Dictionary<string, object?>
                {
                    ["Rating"] = r.Reference, ["Shipment"] = r.ShipmentReference, ["Kept"] = r.Committed ? "Yes" : "No", ["Found a rate"] = r.Qualified ? "Yes" : "No", ["Why not"] = r.ErrorCode, ["Lane"] = r.Lane, ["Service"] = r.Service.ToString(),
                    ["Shipment date"] = r.ShipmentDate, ["Transporter"] = r.TransporterId is { } id && names.TryGetValue(id, out var n) ? n.LegalName : null, ["Contract"] = r.ContractReference, ["Contract version"] = r.ContractRevision,
                    ["Rate"] = r.RateCode, ["Rate version"] = r.RateVersion, ["DPH rule"] = r.DphRuleCode, ["Base"] = r.BaseFreight, ["DPH"] = r.DphAdjustment, ["Accessorials"] = r.AccessorialAmount, ["Discount"] = r.DiscountAmount,
                    ["Total"] = r.TotalFreight, ["Override"] = r.OverrideAmount, ["Override reason"] = r.OverrideReason, ["Calculation version"] = r.CalculationVersion, ["Calculated at"] = r.CalculatedAt.ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                }).ToList();
            }

            case "rate-usage":
            {
                var usage = await dashboard.UsageAsync(12, transporterId, ct);
                return usage.IsFailure ? [] : usage.Value.Select(u => new Dictionary<string, object?>
                    { ["Contract"] = u.ContractNumber, ["Version"] = u.ContractRevision, ["Rate"] = u.RateCode, ["Rate version"] = u.RateVersion, ["Lane"] = u.Lane, ["Shipments"] = u.Shipments, ["Freight rated"] = u.TotalFreight, ["Average freight"] = u.AverageFreight }).ToList();
            }

            default: // lane-comparison, transporter-comparison
            {
                var list = await db.RateCards.AsNoTracking().Join(contracts.Where(c => c.Status == ContractStatus.Active && c.EffectiveFrom <= today && c.EffectiveTo >= today), r => r.ContractId, c => c.Id, (r, c) => new { Card = r, Contract = c }).Take(50_000).ToListAsync(ct);
                var names = await transporters.GetAsync(list.Select(x => x.Contract.TransporterId), ct);
                var rows = list.Select(x => new
                {
                    Lane = $"{x.Card.Origin} → {x.Card.Destination}", Service = x.Card.Pricing.ServiceType().ToString(), Transporter = names.TryGetValue(x.Contract.TransporterId, out var n) ? n.LegalName : "Unknown",
                    Contract = x.Contract.Reference, x.Card.Code, Summary = RateSummary.Describe(x.Card.Pricing), Headline = ImpactHandler.Headline(x.Card.Pricing), x.Card.Priority,
                }).ToList();
                return (report == "lane-comparison" ? rows.OrderBy(r => r.Lane).ThenBy(r => r.Service).ThenBy(r => r.Headline) : rows.OrderBy(r => r.Transporter).ThenBy(r => r.Lane))
                    .Select(r => new Dictionary<string, object?> { ["Lane"] = r.Lane, ["Service"] = r.Service, ["Transporter"] = r.Transporter, ["Contract"] = r.Contract, ["Rate"] = r.Code, ["Rate summary"] = r.Summary, ["Headline rate"] = r.Headline, ["Priority"] = r.Priority }).ToList();
            }
        }
    }
}
