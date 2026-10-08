using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.India;

namespace Tms.Modules.Transporters.Integration;

/// <summary>
/// What Reports &amp; Analytics reads from Transporter Management: the carriers, their scorecards (calculated here, shown there, never recalculated), vehicle placements,
/// load execution times with delay attribution, and alerts. Read-only.
/// </summary>
internal sealed class TransportersReportingProvider(TransportersDbContext db, IVehicleTypeDirectory vehicleTypes) : ITransporterReportingProvider
{
    private static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime.AddMinutes(330));

    private static DateTimeOffset Start(DateOnly d) => new(d.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));

    public async Task<IReadOnlyList<TransporterFact>> TransportersAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var query = db.Transporters.AsNoTracking().AsQueryable();
        if (window.TransporterId is { } own)
        {
            query = query.Where(t => t.Id == own);
        }

        var list = await query.ToListAsync(cancellationToken);
        var vehicles = await db.Vehicles.AsNoTracking().Where(v => v.IsActive).GroupBy(v => v.TransporterId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, cancellationToken);
        var drivers = await db.Drivers.AsNoTracking().GroupBy(d => d.TransporterId).Select(g => new { Id = g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.N, cancellationToken);
        return list.Select(t => new TransporterFact(t.Id, t.Code, t.TradeName ?? t.LegalName, IndiaRegions.OfState(t.State), t.Status == TransporterStatus.Active, vehicles.GetValueOrDefault(t.Id), drivers.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<IReadOnlyList<ScorecardFact>> ScorecardsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var query = db.Scorecards.AsNoTracking().Where(s => s.PeriodEnd >= window.From && s.PeriodEnd <= window.To);
        if (window.TransporterId is { } own)
        {
            query = query.Where(s => s.TransporterId == own);
        }

        var cards = await query.ToListAsync(cancellationToken);
        var names = await db.Transporters.AsNoTracking().Where(t => cards.Select(c => c.TransporterId).Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TradeName ?? t.LegalName, cancellationToken);
        // A scorecard is regenerated, not edited: the newest of each carrier and period is the one in force.
        return cards.GroupBy(c => (c.TransporterId, c.PeriodStart, c.PeriodEnd)).Select(g => g.OrderByDescending(c => c.GeneratedAt).First()).Select(c =>
        {
            decimal? Value(KpiType type) => c.Lines.FirstOrDefault(l => l.Kpi == type)?.Value;
            return new ScorecardFact(c.TransporterId, names.GetValueOrDefault(c.TransporterId) ?? "Unknown transporter", c.PeriodStart, c.PeriodEnd, null, c.OverallScore, Value(KpiType.OnTimePickup), Value(KpiType.OnTimeDelivery),
                Value(KpiType.PlacementCompliance), Value(KpiType.TenderAcceptance), Value(KpiType.PodCompliance), Value(KpiType.ClaimsRate), Value(KpiType.CostPerformance), Value(KpiType.Availability), $"{c.CalculationVersion}.0");
        }).ToList();
    }

    public async Task<IReadOnlyList<PlacementFact>> PlacementsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var from = Start(window.From);
        var to = Start(window.To.AddDays(1));
        var query = db.Placements.AsNoTracking().Where(p => p.RequiredAt >= from && p.RequiredAt < to && p.Status != PlacementStatus.Cancelled);
        if (window.TransporterId is { } own)
        {
            query = query.Where(p => p.TransporterId == own);
        }

        var list = await query.ToListAsync(cancellationToken);
        if (list.Count == 0)
        {
            return [];
        }

        var names = await db.Transporters.AsNoTracking().Where(t => list.Select(p => p.TransporterId).Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TradeName ?? t.LegalName, cancellationToken);
        var types = await vehicleTypes.GetAsync(list.Where(p => p.VehicleTypeId is not null).Select(p => p.VehicleTypeId!.Value).Distinct(), cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var result = new List<PlacementFact>();
        foreach (var p in list.Where(p => p.RequiredAt <= now || p.PlacedAt is not null))
        {
            var outcome = p.Status == PlacementStatus.NoShow ? "NoShow" : p.ReplacementCount > 0 ? "Replaced" : p.PlacedAt is { } placed && placed <= p.RequiredAt ? "OnTime" : "Late";
            var delay = p.PlacedAt is { } at ? (int?)Math.Max(0, (int)(at - p.RequiredAt).TotalMinutes) : p.Status == PlacementStatus.NoShow ? null : (int)Math.Max(0, (now - p.RequiredAt).TotalMinutes);
            result.Add(new PlacementFact($"PLC-{p.ShipmentNumber}", p.ShipmentNumber, p.TransporterId, names.GetValueOrDefault(p.TransporterId) ?? "Unknown transporter", $"{p.OriginCity ?? p.OriginState} → {p.DestinationCity ?? p.DestinationState}",
                p.VehicleTypeId is { } t ? types.GetValueOrDefault(t)?.Name : null, p.Mode == FreightMode.Ftl ? "FTL" : "PTL", p.CreatedAt, p.CreatedAt, p.ReportedAt, p.PlacedAt, p.RequiredAt, outcome, delay));
        }

        return result;
    }

    public async Task<IReadOnlyList<ExceptionFact>> ExceptionsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var query = db.Alerts.AsNoTracking().AsQueryable();
        if (window.TransporterId is { } own)
        {
            query = query.Where(a => a.TransporterId == own);
        }

        var list = await query.OrderByDescending(a => a.CreatedAt).Take(2_000).ToListAsync(cancellationToken);
        var names = await db.Transporters.AsNoTracking().Where(t => list.Select(a => a.TransporterId).Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.TradeName ?? t.LegalName, cancellationToken);
        return list.Select(a => new ExceptionFact("Transporter", a.EntityKey, a.AlertType, a.Severity switch { AlertSeverity.Critical => "Critical", AlertSeverity.High => "High", AlertSeverity.Medium => "Warning", _ => "Info" }, a.ShipmentNumber,
            a.TransporterId, names.GetValueOrDefault(a.TransporterId), null, a.CreatedAt, a.ResolvedAt, null, a.Status switch { AlertStatus.Resolved => "Resolved", AlertStatus.Acknowledged => "InProgress", _ => "Open" })).ToList();
    }

    public async Task<IReadOnlyList<ExecutionFact>> ExecutionsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var from = Start(window.From.AddDays(-1));
        var to = Start(window.To.AddDays(2));
        var query = db.Executions.AsNoTracking().Where(e => e.Status != ExecutionStatus.Cancelled && ((e.PlannedPickupAt >= from && e.PlannedPickupAt < to) || (e.PlannedDeliveryAt >= from && e.PlannedDeliveryAt < to)));
        if (window.TransporterId is { } own)
        {
            query = query.Where(e => e.TransporterId == own);
        }

        string? Who(DelayAttribution a) => a switch { DelayAttribution.Carrier => "Carrier", DelayAttribution.NonCarrier => "NonCarrier", _ => null };
        return (await query.ToListAsync(cancellationToken)).Select(e => new ExecutionFact(e.ShipmentNumber, e.TransporterId, e.PlannedPickupAt, e.ActualPickupAt, Who(e.PickupAttribution), e.PlannedDeliveryAt, e.ActualDeliveryAt, Who(e.DeliveryAttribution))).ToList();
    }
}
