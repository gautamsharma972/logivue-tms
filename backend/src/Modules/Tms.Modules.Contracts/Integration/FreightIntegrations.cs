using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Application.Rating;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Contracts.Integration;

internal static class ServiceMap
{
    public static ContractType ToContract(this FreightServiceType s) => s switch { FreightServiceType.Ptl => ContractType.Ptl, FreightServiceType.Dedicated => ContractType.Dedicated, _ => ContractType.Ftl };

    public static FreightServiceType ToShared(this ContractType s) => s switch { ContractType.Ptl => FreightServiceType.Ptl, ContractType.Dedicated => FreightServiceType.Dedicated, _ => FreightServiceType.Ftl };

    public static RatingRequestDto ToRequest(this FreightRatingRequest r) =>
        new(r.ShipmentDate, new Location(r.OriginState, r.OriginCity), new Location(r.DestinationState, r.DestinationCity), r.Service.ToContract(), r.TransporterId, r.VehicleTypeId,
            r.WeightKg > 0 ? r.WeightKg : null, r.VolumeCbm > 0 ? r.VolumeCbm : null, r.DistanceKm > 0 ? r.DistanceKm : null, Math.Max(1, r.StopCount), r.RequiredCapabilities, r.AccessorialInputs, r.ShipmentReference, r.Commit);
}

/// <summary>Planning's and freight audit's way into the rating engine. The caller authorises its own users; tenant isolation still applies.</summary>
internal sealed class FreightRatingIntegration(RatingService rating, ContractsDbContext db) : IFreightPlanningIntegration, IContractualBaselineService
{
    public async Task<FreightRatingResult> CalculatePlanningFreightAsync(FreightRatingRequest request, CancellationToken cancellationToken = default)
    {
        var (result, _) = await rating.RunAsync(request.ToRequest(), RunMode.Calculate, cancellationToken);
        return RatingMapper.ToFact(result, request.TransporterId);
    }

    public async Task<FreightRatingOptions> GetFreightOptionsAsync(FreightRatingRequest request, CancellationToken cancellationToken = default)
    {
        var (result, _) = await rating.RunAsync(request.ToRequest() with { Commit = false }, RunMode.Simulate, cancellationToken);
        var options = result.Options.Select(o => RatingMapper.ToFact(result with { Selected = o, Options = [o] }, o.TransporterId)).ToList();
        var exclusions = result.Exclusions.Select(e => new RateExclusionFact(e.ContractReference, e.RateReference, e.ReasonCode, e.Reason)).ToList();
        return new FreightRatingOptions(options, exclusions, result.Qualified ? null : result.Message);
    }

    public async Task<ContractualFreightBaseline> GetContractualBaselineAsync(FreightAuditRequest request, CancellationToken cancellationToken = default)
    {
        // A shipment that was rated and kept is judged by that rating, however the contracts have changed since.
        if (!string.IsNullOrWhiteSpace(request.ShipmentReference))
        {
            var reference = request.ShipmentReference.Trim();
            var kept = await db.Ratings.AsNoTracking().Include(r => r.Components).Where(r => r.ShipmentReference == reference && r.Committed && r.Qualified).OrderByDescending(r => r.CalculatedAt).FirstOrDefaultAsync(cancellationToken);
            if (kept is not null)
            {
                return new ContractualFreightBaseline(
                    true, null, kept.ContractReference, kept.ContractRevision, kept.RateCode, kept.RateVersion, kept.BaseFreight, kept.DphAdjustment, kept.AccessorialAmount, kept.DiscountAmount, kept.TotalFreight,
                    kept.Currency, kept.CalculationVersion, true,
                    kept.Components.OrderBy(c => c.Sequence).Select(c => new RatingComponentFact(c.Type, c.Description, c.Quantity, c.Unit, c.Rate, c.Amount, c.Reference, c.Sequence)).ToList());
            }
        }

        if (request.Rating is null)
        {
            return new ContractualFreightBaseline(false, "No rating was kept for that shipment, and no shipment details were given to rate it now.", null, null, null, null, 0, 0, 0, 0, 0, "INR", RatingEngine.Version, false, []);
        }

        var (result, _) = await rating.RunAsync(request.Rating.ToRequest() with { Commit = false }, RunMode.Simulate, cancellationToken);
        var s = result.Selected;
        return s is null
            ? new ContractualFreightBaseline(false, result.Message, null, null, null, null, 0, 0, 0, 0, 0, "INR", result.CalculationVersion, false, [])
            : new ContractualFreightBaseline(
                true, null, s.ContractReference, s.ContractRevision, s.Rate.Code, s.Rate.Version, s.BaseFreight, s.DphAdjustment, s.AccessorialAmount, s.DiscountAmount, s.TotalFreight, s.Currency, result.CalculationVersion, false,
                s.Lines.Select(l => new RatingComponentFact(l.Type, l.Description, l.Quantity, l.Unit, l.Rate, l.Amount, l.Reference, l.Sequence)).ToList());
    }
}

/// <summary>Whether a carrier has a contract that prices a lane. It only reads the rate book; who may be given work stays Transporters' decision.</summary>
internal sealed class TransporterCoverageIntegration(ContractsDbContext db, TimeProvider clock) : IFreightTransporterIntegration
{
    public async Task<bool> HasActiveCommercialCoverageAsync(
        Guid transporterId, string originState, string? originCity, string destinationState, string? destinationCity, FreightServiceType service, DateOnly? date = null, CancellationToken cancellationToken = default)
    {
        var on = date ?? clock.TodayInIndia();
        var origin = new Location(originState, originCity);
        var destination = new Location(destinationState, destinationCity);
        var type = service.ToContract();
        var contracts = await db.Contracts.AsNoTracking().Include(c => c.RateCards)
            .Where(c => c.TransporterId == transporterId && (c.Status == ContractStatus.Active || c.Status == ContractStatus.Suspended || c.Status == ContractStatus.Expired || c.Status == ContractStatus.Superseded || c.Status == ContractStatus.Terminated)
                        && c.EffectiveFrom <= on && c.EffectiveTo >= on).ToListAsync(cancellationToken);
        if (contracts.Count == 0)
        {
            return false;
        }

        var zones = (await db.Zones.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(z => z.Code);
        Zone? Zone(string code) => zones.GetValueOrDefault(code);
        return contracts.Where(c => c.IsInForce(on) && c.EffectiveServices.Contains(type)).Any(c => c.RateCards.Any(r =>
            r.Pricing.ServiceType() == type && (r.ValidFrom is null || r.ValidFrom <= on) && (r.ValidTo is null || r.ValidTo >= on)
            && ((r.Origin.Matches(origin, Zone) && r.Destination.Matches(destination, Zone)) || (r.BothWays && r.Origin.Matches(destination, Zone) && r.Destination.Matches(origin, Zone)))));
    }
}

/// <summary>What the contracts commit to: vehicles, trips, tonnage, business share and service levels. Planning and transporter management read it.</summary>
internal sealed class ContractedCapacityProvider(ContractsDbContext db, ITransporterDirectoryLookup lookup) : IContractedCapacityProvider
{
    public async Task<IReadOnlyList<ContractedCapacityFact>> GetCapacityAsync(Guid? transporterId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var contracts = await InForceAsync(transporterId, date, cancellationToken);
        var ids = contracts.Select(c => c.Id).ToList();
        var capacities = await db.Capacities.AsNoTracking().Where(c => ids.Contains(c.ContractId)).ToListAsync(cancellationToken);
        var types = await lookup.VehicleTypesAsync(capacities.Where(c => c.Spec.VehicleTypeId.HasValue).Select(c => c.Spec.VehicleTypeId!.Value), cancellationToken);
        var byId = contracts.ToDictionary(c => c.Id);
        return capacities.Where(c => (c.Spec.ValidFrom is null || c.Spec.ValidFrom <= date) && (c.Spec.ValidTo is null || c.Spec.ValidTo >= date))
            .Select(c => new ContractedCapacityFact(
                c.ContractId, byId[c.ContractId].Reference, byId[c.ContractId].TransporterId, c.Spec.VehicleTypeId, c.Spec.VehicleTypeId is { } v && types.TryGetValue(v, out var t) ? t : null,
                c.Spec.CommittedVehicleCount, c.Spec.CommittedCapacityKg, c.Spec.MinimumMonthlyTrips, c.Spec.MinimumMonthlyTonnage, c.Spec.TargetBusinessSharePct, c.Spec.ValidFrom, c.Spec.ValidTo)).ToList();
    }

    public async Task<IReadOnlyList<ContractSlaFact>> GetSlaAsync(Guid? transporterId, DateOnly date, CancellationToken cancellationToken = default)
    {
        var contracts = await InForceAsync(transporterId, date, cancellationToken);
        var ids = contracts.Select(c => c.Id).ToList();
        var slas = await db.Slas.AsNoTracking().Where(c => ids.Contains(c.ContractId)).ToListAsync(cancellationToken);
        var byId = contracts.ToDictionary(c => c.Id);
        return slas.Select(s => new ContractSlaFact(
            s.ContractId, byId[s.ContractId].Reference, byId[s.ContractId].TransporterId, s.Spec.Service.ToShared(), s.Spec.Origin?.State ?? s.Spec.Origin?.ZoneCode, s.Spec.Origin?.City, s.Spec.Destination?.State ?? s.Spec.Destination?.ZoneCode, s.Spec.Destination?.City,
            s.Spec.PickupSlaMinutes, s.Spec.TransitSlaMinutes, s.Spec.DeliverySlaMinutes, s.Spec.TenderLeadTimeMinutes, (s.Spec.OperatingDays ?? []).Select(d => d.ToString()).ToList(), s.Spec.CutoffTime)).ToList();
    }

    private async Task<List<Contract>> InForceAsync(Guid? transporterId, DateOnly date, CancellationToken cancellationToken)
    {
        var contracts = db.Contracts.AsNoTracking().Where(c => (c.Status == ContractStatus.Active || c.Status == ContractStatus.Suspended) && c.EffectiveFrom <= date && c.EffectiveTo >= date);
        if (transporterId is { } t)
        {
            contracts = contracts.Where(c => c.TransporterId == t);
        }

        return (await contracts.Take(2_000).ToListAsync(cancellationToken)).Where(c => c.IsInForce(date)).ToList();
    }
}

/// <summary>Vehicle-type names for the capacity feed, behind the shared directory.</summary>
internal interface ITransporterDirectoryLookup
{
    Task<IReadOnlyDictionary<Guid, string>> VehicleTypesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken);
}

internal sealed class DirectoryLookup(IVehicleTypeDirectory vehicleTypes) : ITransporterDirectoryLookup
{
    public async Task<IReadOnlyDictionary<Guid, string>> VehicleTypesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken) =>
        (await vehicleTypes.GetAsync(ids.Distinct(), cancellationToken)).ToDictionary(p => p.Key, p => p.Value.Name);
}

/// <summary>
/// The stand-in for "what really happened on a shipment": nothing supplies it yet, so rating works from the request alone. POD / Delivery and Tracking can implement
/// <see cref="IFreightActualsProvider"/> later without Contracts knowing about either.
/// </summary>
internal sealed class NoFreightActuals : IFreightActualsProvider
{
    public Task<FreightActuals?> GetActualsAsync(string shipmentReference, CancellationToken cancellationToken = default) => Task.FromResult<FreightActuals?>(null);
}
