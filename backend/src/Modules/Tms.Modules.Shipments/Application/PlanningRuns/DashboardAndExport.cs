using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MiniExcelLibs;
using Tms.Modules.Shipments.Domain;
using Tms.Modules.Shipments.Infrastructure.Export;
using Tms.Modules.Shipments.Infrastructure.Persistence;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Application.PlanningRuns;

public sealed record DashboardQuery(DateOnly? From = null, DateOnly? To = null);

public sealed record DashboardDto(DateOnly From, DateOnly To, PlanningKpis Kpis);

internal sealed record ExportFile(byte[] Content, string ContentType, string FileName);

internal sealed class DashboardHandler(ShipmentsDbContext db, ShipmentAccess access, TimeProvider clock)
{
    public const int MaxDays = 366;
    private const int MaxPlans = 1000;

    /// <summary>KPIs over the newest version of each plan whose planning date falls in the period. Cancelled plans are left out.</summary>
    public async Task<Result<DashboardDto>> HandleAsync(DashboardQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanRead)
        {
            return ShipmentAccess.Forbidden;
        }

        var to = query.To ?? clock.TodayInIndia();
        var from = query.From ?? to.AddDays(-30);
        if (to < from || to.DayNumber - from.DayNumber > MaxDays)
        {
            return Error.Validation("planning.period_invalid", $"Choose a period of at most {MaxDays} days, ending after it starts.");
        }

        var runs = await db.PlanningRuns.AsNoTracking()
            .Where(r => r.PlanningDate >= from && r.PlanningDate <= to && r.Status != PlanStatus.Cancelled && r.Status != PlanStatus.Running
                && !db.PlanningRuns.Any(n => n.RunGroupId == r.RunGroupId && n.PlanVersion > r.PlanVersion))
            .OrderByDescending(r => r.PlanningDate).ThenByDescending(r => r.CreatedAt)
            .Take(MaxPlans)
            .ToListAsync(cancellationToken);

        return new DashboardDto(from, to, PlanningKpiCalculator.Calculate(runs.Select(r => new KpiPlan(r.PlanningDate, r.Status, r.Plan)).ToList()));
    }
}

internal sealed class ExportHandler(RunLoader loader, DashboardHandler dashboard)
{
    public async Task<Result<ExportFile>> RunAsync(Guid id, string? format, CancellationToken cancellationToken)
    {
        if (!PlanExport.IsKnown(format))
        {
            return PlanExport.UnknownFormat;
        }

        var found = await loader.FindAsync(id, write: false, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error;
        }

        var run = found.Value.ToDto(await loader.IsLatestAsync(found.Value, cancellationToken));
        return await PlanExport.RunAsync(run, format!);
    }

    public async Task<Result<ExportFile>> DashboardAsync(DashboardQuery query, string? format, CancellationToken cancellationToken)
    {
        if (!PlanExport.IsKnown(format))
        {
            return PlanExport.UnknownFormat;
        }

        var result = await dashboard.HandleAsync(query, cancellationToken);
        return result.IsFailure ? result.Error : await PlanExport.DashboardAsync(result.Value, format!);
    }
}

/// <summary>CSV and Excel files of a plan or the KPIs. Text a spreadsheet could read as a formula is neutralised.</summary>
internal static class PlanExport
{
    public static readonly Error UnknownFormat = Error.Validation("export.format_invalid", "Choose csv, xlsx or pdf.");

    private const string CsvType = "text/csv; charset=utf-8";
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static bool IsKnown(string? format) =>
        format is not null && (format.Equals("csv", StringComparison.OrdinalIgnoreCase) || format.Equals("xlsx", StringComparison.OrdinalIgnoreCase) || format.Equals("pdf", StringComparison.OrdinalIgnoreCase));

    private static bool IsPdf(string format) => format.Equals("pdf", StringComparison.OrdinalIgnoreCase);

    private static bool IsCsv(string format) => format.Equals("csv", StringComparison.OrdinalIgnoreCase);

    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    /// <summary>A leading =, +, - or @ makes Excel evaluate a cell; a leading apostrophe-style guard keeps it as text.</summary>
    internal static string Safe(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;

    private static string Time(DateTimeOffset? t) => t is { } x ? x.ToOffset(India).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty;

    private static decimal? Pct(decimal? fraction) => fraction is { } f ? Math.Round(f * 100, 1) : null;

    /// <summary>The columns of each sheet that fit a printed page, in order. A sheet not listed is left out of the PDF.</summary>
    private static ExportFile Pdf(string baseName, string title, string subtitle, Dictionary<string, object> sheets, IReadOnlyDictionary<string, string[]> columns)
    {
        var pdf = new SimplePdf(title, subtitle);
        foreach (var (name, picked) in columns)
        {
            if (!sheets.TryGetValue(name, out var sheet) || sheet is not List<Dictionary<string, object?>> rows || rows.Count == 0)
            {
                continue;
            }

            pdf.Heading(name);
            var keys = picked.Length == 0 ? rows[0].Keys.ToArray() : picked.Where(rows[0].ContainsKey).ToArray();
            var numeric = keys.Select(k => rows.Any(r => r.GetValueOrDefault(k) is decimal or double or int or long) && !k.Equals("Value", StringComparison.Ordinal)).ToList();
            pdf.Table(keys, rows.Select(r => (IReadOnlyList<string>)keys.Select(k => Format(r.GetValueOrDefault(k))).ToList()).ToList(), numeric);
        }

        return new ExportFile(pdf.ToBytes(DateTimeOffset.UtcNow.ToOffset(India)), "application/pdf", $"{baseName}.pdf");
    }

    private static async Task<ExportFile> Build(string format, string baseName, string csvSheet, Dictionary<string, object> sheets)
    {
        if (IsCsv(format))
        {
            return new ExportFile(Csv((IEnumerable<Dictionary<string, object?>>)sheets[csvSheet]), CsvType, $"{baseName}.csv");
        }

        using var stream = new MemoryStream();
        await MiniExcel.SaveAsAsync(stream, sheets);
        return new ExportFile(stream.ToArray(), XlsxType, $"{baseName}.xlsx");
    }

    private static byte[] Csv(IEnumerable<Dictionary<string, object?>> rows)
    {
        var list = rows.ToList();
        var sb = new StringBuilder();
        if (list.Count == 0)
        {
            return [];
        }

        var columns = list[0].Keys.ToList();
        sb.AppendJoin(',', columns.Select(Quote)).Append("\r\n");
        foreach (var row in list)
        {
            sb.AppendJoin(',', columns.Select(c => Quote(Format(row.GetValueOrDefault(c))))).Append("\r\n");
        }

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(sb.ToString()); // BOM so Excel reads ₹ and accents
    }

    private static string Format(object? v) => v switch
    {
        null => string.Empty,
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };

    private static string Quote(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : value;

    public static Task<ExportFile> RunAsync(RunDto run, string format)
    {
        var plan = run.Plan;
        var vehicles = plan.Vehicles.Select((v, i) => (V: v, N: i + 1)).ToList();

        var orders = new List<Dictionary<string, object?>>();
        foreach (var (v, n) in vehicles)
        {
            foreach (var o in v.Orders.OrderBy(x => x.Sequence))
            {
                orders.Add(new Dictionary<string, object?>
                {
                    ["Plan"] = run.Number, ["Version"] = run.PlanVersion, ["Status"] = "Planned", ["Vehicle"] = n, ["Mode"] = v.Mode.ToString(), ["Vehicle type"] = Safe(v.VehicleTypeName),
                    ["Transporter"] = Safe(v.TransporterName), ["Contract"] = Safe(v.ContractReference), ["Order"] = Safe(o.Number),
                    ["Role"] = o.Kind == "ReturnPickup" ? "Return pickup" : "Delivery", ["Stop"] = o.Sequence, ["Place"] = Safe($"{o.DropCity}, {o.DropState}"),
                    ["Weight kg"] = o.WeightKg, ["Volume CBM"] = o.VolumeCbm, ["Vehicle cost INR"] = v.EstimatedCost,
                    ["Weight utilisation %"] = Pct(v.WeightUtilisation), ["Volume utilisation %"] = Pct(v.VolumeUtilisation), ["Distance km"] = v.DistanceKm, ["Reason"] = string.Empty,
                });
            }
        }

        foreach (var u in plan.Unplanned)
        {
            orders.Add(new Dictionary<string, object?>
            {
                ["Plan"] = run.Number, ["Version"] = run.PlanVersion, ["Status"] = "Unplanned", ["Vehicle"] = null, ["Mode"] = string.Empty, ["Vehicle type"] = string.Empty,
                ["Transporter"] = string.Empty, ["Contract"] = string.Empty, ["Order"] = Safe(u.Number), ["Role"] = string.Empty, ["Stop"] = null, ["Place"] = string.Empty,
                ["Weight kg"] = null, ["Volume CBM"] = null, ["Vehicle cost INR"] = null, ["Weight utilisation %"] = null, ["Volume utilisation %"] = null, ["Distance km"] = null,
                ["Reason"] = Safe($"{u.Code}: {u.Reason}"),
            });
        }

        var s = plan.Summary;
        var sheets = new Dictionary<string, object>
        {
            ["Summary"] = new List<Dictionary<string, object?>>
            {
                Metric("Plan", run.Number), Metric("Version", run.PlanVersion), Metric("Planning date", run.PlanningDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Metric("Status", run.Status.ToString()), Metric("Solver", plan.SolverStatus.ToString()), Metric("Orders planned", s.OrdersPlanned), Metric("Orders unplanned", s.OrdersUnplanned),
                Metric("Vehicles", s.VehiclesUsed), Metric("Full truck / part load", $"{s.FtlCount} / {s.PtlCount}"), Metric("Total freight INR", s.TotalCost),
                Metric("Average weight utilisation %", Pct(s.AverageWeightUtilisation)), Metric("Average volume utilisation %", Pct(s.AverageVolumeUtilisation)),
                Metric("Total distance km", s.TotalDistanceKm), Metric("Cost per tonne-km INR", s.CostPerTonneKm), Metric("Consolidation saving INR", s.ConsolidationSaving),
                Metric("Return-load saving INR", s.BackhaulSaving),
                Metric("Loaded km", s.TotalLoadedKm), Metric("Empty km", s.TotalEmptyKm), Metric("Empty km %", s.EmptyKmPercent),
                Metric("Cost per tonne INR", s.CostPerTonne), Metric("Cost per shipment INR", s.CostPerShipment),
            },
            ["Vehicles"] = vehicles.Select(x => new Dictionary<string, object?>
            {
                ["Vehicle"] = x.N, ["Mode"] = x.V.Mode.ToString(), ["Vehicle type"] = Safe(x.V.VehicleTypeName), ["Transporter"] = Safe(x.V.TransporterName), ["Contract"] = Safe(x.V.ContractReference),
                ["Pickup"] = Safe($"{x.V.PickupCity}, {x.V.PickupState}"), ["Orders"] = x.V.Orders.Count, ["Weight kg"] = x.V.WeightKg, ["Volume CBM"] = x.V.VolumeCbm,
                ["Weight utilisation %"] = Pct(x.V.WeightUtilisation), ["Volume utilisation %"] = Pct(x.V.VolumeUtilisation), ["Distance km"] = x.V.DistanceKm,
                ["Cost INR"] = x.V.EstimatedCost, ["Cost per tonne-km INR"] = x.V.CostPerTonneKm, ["Consolidation saving INR"] = x.V.ConsolidationSaving,
                ["Return-load saving INR"] = x.V.BackhaulSaving, ["Loaded km"] = x.V.LoadedKm, ["Empty km"] = x.V.EmptyKm,
                ["Assigned vehicle"] = Safe(x.V.AssignedVehicle?.Registration), ["Driver"] = Safe(x.V.AssignedDriver?.Name), ["Driver phone"] = Safe(x.V.AssignedDriver?.Phone), ["Locked"] = x.V.IsLocked ? "Yes" : "No", ["Why"] = Safe(x.V.Reason),
            }).ToList(),
            ["Orders"] = orders,
            ["Stops"] = vehicles.SelectMany(x => (x.V.Stops ?? []).Select(st => new Dictionary<string, object?>
            {
                ["Vehicle"] = x.N, ["Stop"] = st.Sequence, ["Kind"] = st.Kind, ["Place"] = Safe(st.Label), ["Arrive"] = Time(st.PlannedArrival), ["Depart"] = Time(st.PlannedDeparture), ["Wait min"] = st.WaitMinutes,
            })).ToList(),
            ["Unplanned"] = plan.Unplanned.Select(u => new Dictionary<string, object?>
            {
                ["Order"] = Safe(u.Number), ["Code"] = u.Code, ["Reason"] = Safe(u.Reason), ["Suggestions"] = Safe(string.Join("; ", u.Suggestions)),
            }).ToList(),
        };

        if (IsPdf(format))
        {
            return Task.FromResult(Pdf(
                $"{run.Number}-v{run.PlanVersion}", $"Transport plan {run.Number} (version {run.PlanVersion})",
                $"Planning date {run.PlanningDate:dd MMM yyyy} | {run.Status} | {plan.SolverMessage}", sheets,
                new Dictionary<string, string[]>
                {
                    ["Summary"] = [],
                    ["Vehicles"] = ["Vehicle", "Mode", "Vehicle type", "Transporter", "Assigned vehicle", "Driver", "Driver phone", "Orders", "Weight kg", "Cost INR", "Distance km", "Empty km"],
                    ["Orders"] = ["Vehicle", "Order", "Role", "Stop", "Place", "Weight kg", "Status", "Reason"],
                    ["Stops"] = ["Vehicle", "Stop", "Kind", "Place", "Arrive", "Depart", "Wait min"],
                    ["Unplanned"] = ["Order", "Code", "Reason", "Suggestions"],
                }));
        }

        return Build(format, $"{run.Number}-v{run.PlanVersion}", "Orders", sheets);
    }

    public static Task<ExportFile> DashboardAsync(DashboardDto d, string format)
    {
        var k = d.Kpis;
        var kpis = new List<Dictionary<string, object?>>
        {
            Metric("Period", $"{d.From:yyyy-MM-dd} to {d.To:yyyy-MM-dd}"), Metric("Plans", k.Plans), Metric("Total orders", k.OrdersTotal), Metric("Planned orders", k.OrdersPlanned),
            Metric("Unplanned orders", k.OrdersUnplanned), Metric("Vehicles used", k.VehiclesUsed), Metric("Total freight INR", k.TotalFreightCost),
            Metric("Consolidation saving INR", k.ConsolidationSaving), Metric("Return-load saving INR", k.BackhaulSaving), Metric("Total savings INR", k.TotalSavings),
            Metric("Average weight utilisation %", Pct(k.AverageWeightUtilisation)), Metric("Average volume utilisation %", Pct(k.AverageVolumeUtilisation)),
            Metric("Total distance km", k.TotalDistanceKm), Metric("Cost per tonne-km INR", k.CostPerTonneKm), Metric("Average stops per vehicle", k.AverageStopsPerVehicle),
            Metric("Loaded km", k.TotalLoadedKm), Metric("Empty km", k.TotalEmptyKm), Metric("Empty km %", k.EmptyKmPercent),
            Metric("Cost per tonne INR", k.CostPerTonne), Metric("Cost per shipment INR", k.CostPerShipment),
            Metric("Full truck %", k.FtlPercent), Metric("Part load %", k.PtlPercent), Metric("Consolidated shipments %", k.ConsolidatedPercent), Metric("Return pickups %", k.ReturnPickupPercent),
        };
        var sheets = new Dictionary<string, object>
        {
            ["KPIs"] = kpis,
            ["Daily"] = k.Daily.Select(x => new Dictionary<string, object?>
            {
                ["Date"] = x.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["Plans"] = x.Plans, ["Orders planned"] = x.OrdersPlanned, ["Orders unplanned"] = x.OrdersUnplanned,
                ["Vehicles"] = x.Vehicles, ["Freight INR"] = x.Cost, ["Average weight utilisation %"] = Pct(x.AverageWeightUtilisation),
            }).ToList(),
            ["Unplanned reasons"] = k.UnplannedReasons.Select(x => new Dictionary<string, object?> { ["Reason"] = x.Code, ["Orders"] = x.Orders }).ToList(),
            ["Transporters"] = k.Transporters.Select(x => new Dictionary<string, object?> { ["Transporter"] = Safe(x.Name), ["Vehicles"] = x.Vehicles, ["Freight INR"] = x.Cost }).ToList(),
            ["Vehicle types"] = k.VehicleTypes.Select(x => new Dictionary<string, object?> { ["Vehicle type"] = Safe(x.Name), ["Vehicles"] = x.Vehicles }).ToList(),
        };
        if (IsPdf(format))
        {
            return Task.FromResult(Pdf(
                $"planning-kpis-{d.From:yyyyMMdd}-{d.To:yyyyMMdd}", "Planning KPIs", $"{d.From:dd MMM yyyy} to {d.To:dd MMM yyyy} | latest version of each plan", sheets,
                new Dictionary<string, string[]>
                {
                    ["KPIs"] = [],
                    ["Daily"] = [],
                    ["Unplanned reasons"] = [],
                    ["Transporters"] = [],
                    ["Vehicle types"] = [],
                }));
        }

        return Build(format, $"planning-kpis-{d.From:yyyyMMdd}-{d.To:yyyyMMdd}", "KPIs", sheets);
    }

    private static Dictionary<string, object?> Metric(string name, object? value) => new() { ["Metric"] = name, ["Value"] = value is string s ? Safe(s) : value };
}
