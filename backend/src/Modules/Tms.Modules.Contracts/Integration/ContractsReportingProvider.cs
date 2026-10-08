using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Contracts.Integration;

/// <summary>
/// What Reports &amp; Analytics reads from freight contracts: contracts and renewal status, rates and slabs, diesel clauses, and kept ratings exactly as they were made.
/// Read-only; ratings are historical records, so later changes to a contract never alter what is reported for a past shipment.
/// </summary>
internal sealed class ContractsReportingProvider(ContractsDbContext db, ITransporterDirectory transporters, IVehicleTypeDirectory vehicleTypes) : IFreightContractReportingProvider
{
    private static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime.AddMinutes(330));

    private static string Service(ContractType type) => type switch { ContractType.Ftl => "FTL", ContractType.Ptl => "PTL", _ => "Dedicated" };

    private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        (await transporters.GetAsync(ids.Distinct(), cancellationToken)).ToDictionary(t => t.Key, t => t.Value.LegalName);

    private IQueryable<Contract> Live(ReportingWindow w)
    {
        var query = db.Contracts.AsNoTracking().Where(c => c.Status != ContractStatus.Cancelled && c.Status != ContractStatus.Rejected);
        return w.TransporterId is { } own ? query.Where(c => c.TransporterId == own) : query;
    }

    public async Task<IReadOnlyList<ContractFact>> ContractsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var list = await Live(window).ToListAsync(cancellationToken);
        var names = await NamesAsync(list.Select(c => c.TransporterId), cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330));
        return list.Select(c => new ContractFact(c.Reference, c.Id, c.Revision, c.TransporterId, names.GetValueOrDefault(c.TransporterId) ?? "Unknown transporter", string.Join("/", c.EffectiveServices.Select(Service)), c.EffectiveFrom, c.EffectiveTo,
            c.Status.ToString(), c.Status == ContractStatus.Superseded ? "Renewed" : c.Status == ContractStatus.Expired ? "Lapsed" : c.Status == ContractStatus.Active && c.EffectiveTo.DayNumber - today.DayNumber <= c.RenewalNoticeDays ? "Due" : "None", c.RateCount)).ToList();
    }

    public async Task<IReadOnlyList<RateFact>> RatesAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var contracts = (await Live(window).Where(c => c.Status != ContractStatus.Superseded).ToListAsync(cancellationToken)).ToDictionary(c => c.Id);
        var ids = contracts.Keys.ToList();
        var rates = await db.RateCards.AsNoTracking().Where(r => ids.Contains(r.ContractId)).ToListAsync(cancellationToken);
        var names = await NamesAsync(contracts.Values.Select(c => c.TransporterId), cancellationToken);
        var types = await vehicleTypes.GetAsync(rates.Where(r => r.VehicleTypeId is not null).Select(r => r.VehicleTypeId!.Value).Distinct(), cancellationToken);
        return rates.Select(r =>
        {
            var c = contracts[r.ContractId];
            var (rate, basis) = r.Pricing switch
            {
                FlatTripPricing f => (f.AmountPerTrip, "PerTrip"),
                PerKmPricing p => (p.RatePerKm, "PerKm"),
                WeightSlabPricing w => (w.Slabs.Count > 0 ? w.Slabs[0].RatePerKg : 0m, "PerKg"),
                SlabRatePricing s => (s.Slabs.Count > 0 ? s.Slabs[0].Rate : 0m, s.Unit.ToString()),
                DedicatedPricing d => (d.MonthlyRental, "PerMonth"),
                _ => (0m, "Other"),
            };
            string? Slab(decimal? from, decimal? to, string unit) => from is null && to is null ? null : $"{from?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "0"}-{to?.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) ?? "+"} {unit}";
            return new RateFact(c.Reference, c.Revision, c.TransporterId, names.GetValueOrDefault(c.TransporterId) ?? "Unknown transporter", r.Code, r.Version, r.OriginCity ?? r.OriginZone ?? r.OriginState ?? "Anywhere",
                r.DestinationCity ?? r.DestinationZone ?? r.DestinationState ?? "Anywhere", r.OriginZone ?? r.DestinationZone, r.VehicleTypeId is { } t ? types.GetValueOrDefault(t)?.Name : null, Service(r.Pricing.ServiceType()),
                Slab(r.MinWeightKg, r.MaxWeightKg, "kg"), Slab(r.MinDistanceKm, r.MaxDistanceKm, "km"), Slab(r.MinVolumeCbm, r.MaxVolumeCbm, "CBM"), rate, basis, r.MinimumCharge, r.MaximumCharge, r.ValidFrom, r.ValidTo);
        }).ToList();
    }

    public async Task<IReadOnlyList<DphFact>> DphAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var contracts = (await Live(window).Where(c => c.Status != ContractStatus.Superseded).ToListAsync(cancellationToken)).ToDictionary(c => c.Id);
        var ids = contracts.Keys.ToList();
        var rules = await db.DphRules.AsNoTracking().Where(r => ids.Contains(r.ContractId)).ToListAsync(cancellationToken);
        var accessorials = (await db.ContractAccessorials.AsNoTracking().Where(a => ids.Contains(a.ContractId)).ToListAsync(cancellationToken)).GroupBy(a => a.ContractId).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(a => a.Spec.Code)));
        var prices = await db.DieselPrices.AsNoTracking().ToListAsync(cancellationToken);
        var names = await NamesAsync(contracts.Values.Select(c => c.TransporterId), cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330));
        return rules.Select(r =>
        {
            var c = contracts[r.ContractId];
            var current = DieselPrice.Resolve(prices, r.Spec.Region, today) ?? r.Spec.BaseDieselPrice;
            var variation = r.Spec.BaseDieselPrice == 0 ? 0m : Math.Round((current - r.Spec.BaseDieselPrice) / r.Spec.BaseDieselPrice * 100m, 2);
            var adjustment = Math.Abs(variation) < r.Spec.ThresholdPercent ? 0m : Math.Round(variation * r.Spec.FuelComponentPercent / 100m, 2);
            if (r.Spec.CapPercent is { } cap && Math.Abs(adjustment) > cap)
            {
                adjustment = Math.Sign(adjustment) * cap;
            }

            return new DphFact(c.Reference, c.TransporterId, names.GetValueOrDefault(c.TransporterId) ?? "Unknown transporter", r.Spec.BaseDieselPrice, current, variation, r.Spec.FuelComponentPercent, adjustment,
                r.Spec.EffectiveFrom ?? r.Spec.BaseDate, accessorials.GetValueOrDefault(c.Id) ?? string.Empty);
        }).ToList();
    }

    public async Task<IReadOnlyList<RatingFact>> RatingsAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        var from = new DateTimeOffset(window.From.ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
        var to = new DateTimeOffset(window.To.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.FromMinutes(330));
        var query = db.Ratings.AsNoTracking().Where(r => r.Committed && r.Qualified && r.ShipmentReference != null && r.CalculatedAt >= from && r.CalculatedAt < to);
        if (window.TransporterId is { } own)
        {
            query = query.Where(r => r.TransporterId == own);
        }

        var list = await query.ToListAsync(cancellationToken);
        var names = await NamesAsync(list.Where(r => r.TransporterId is not null).Select(r => r.TransporterId!.Value), cancellationToken);
        var types = await vehicleTypes.GetAsync(list.Where(r => r.VehicleTypeId is not null).Select(r => r.VehicleTypeId!.Value).Distinct(), cancellationToken);
        return list.Select(r =>
        {
            string? slab = null;
            try
            {
                slab = JsonSerializer.Deserialize<List<string>>(r.ReasonsJson)?.FirstOrDefault(x => x.Contains("slab", StringComparison.OrdinalIgnoreCase));
            }
            catch (JsonException)
            {
                // reasons are explanatory text; a malformed one just leaves the slab unnamed
            }

            return new RatingFact(r.ShipmentReference!, r.TransporterId ?? Guid.Empty, r.TransporterId is { } t ? names.GetValueOrDefault(t) ?? "Unknown transporter" : "Unknown transporter", r.ContractReference ?? "-", r.ContractRevision ?? 1,
                r.RateCode ?? "-", r.RateVersion ?? 1, r.Lane, r.WeightKg, r.DistanceKm, r.VolumeCbm, slab, r.BaseFreight, r.DphAdjustment, r.AccessorialAmount, r.DiscountAmount, r.TotalFreight, r.CalculationVersion, r.CalculatedAt,
                Service(r.Service), r.VehicleTypeId is { } v ? types.GetValueOrDefault(v)?.Name : null);
        }).ToList();
    }

    /// <summary>Lanes this module has rated, and how they are covered. Lanes with loads but no rating at all are added by Reports from the shipments it sees.</summary>
    public async Task<IReadOnlyList<CoverageFact>> CoverageAsync(ReportingWindow window, CancellationToken cancellationToken = default)
    {
        if (window.TransporterId is not null)
        {
            return [];
        }

        var ratings = await RatingsAsync(window, cancellationToken);
        var rates = await RatesAsync(window, cancellationToken);
        return ratings.GroupBy(r => (r.Lane, Service: r.Service ?? "FTL")).Select(g =>
        {
            var parts = g.Key.Lane.Split('→', StringSplitOptions.TrimEntries);
            var origin = parts[0];
            var destination = parts.Length > 1 ? parts[1] : parts[0];
            var lane = rates.Count(r => r.Origin == origin && r.Destination == destination);
            return new CoverageFact(g.Key.Lane, origin, destination, g.Key.Service, g.Count(), lane > 0 ? "Lane" : "Zone", lane, rates.Count(r => r.Zone is not null), 0);
        }).ToList();
    }
}
