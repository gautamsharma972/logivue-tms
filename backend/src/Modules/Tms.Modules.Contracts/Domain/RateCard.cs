using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

/// <summary>What an operator enters for one rate: where it applies, for which vehicle/distance, and how it is priced.</summary>
public sealed record RateCardSpec(
    Place Origin,
    Place Destination,
    bool BothWays,
    Guid? VehicleTypeId,
    decimal? MinDistanceKm,
    decimal? MaxDistanceKm,
    Pricing Pricing);

/// <summary>
/// One rate within a contract. Not audited row-by-row (a contract can hold thousands); the contract records that its
/// rates changed and how many there now are.
/// </summary>
[AuditIgnore]
public sealed class RateCard : Entity, ITenantScoped
{
    private RateCard()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid ContractId { get; private set; }

    public PlaceKind OriginKind { get; private set; }

    public string? OriginState { get; private set; }

    public string? OriginCity { get; private set; }

    public string? OriginZone { get; private set; }

    public PlaceKind DestinationKind { get; private set; }

    public string? DestinationState { get; private set; }

    public string? DestinationCity { get; private set; }

    public string? DestinationZone { get; private set; }

    public bool BothWays { get; private set; }

    public Guid? VehicleTypeId { get; private set; }

    public decimal? MinDistanceKm { get; private set; }

    public decimal? MaxDistanceKm { get; private set; }

    public Pricing Pricing { get; private set; } = null!;

    public Place Origin => Place.From(OriginKind, OriginState, OriginCity, OriginZone).Value;

    public Place Destination => Place.From(DestinationKind, DestinationState, DestinationCity, DestinationZone).Value;

    public bool HasDistanceBand => MinDistanceKm is not null || MaxDistanceKm is not null;

    internal static RateCard Create(Guid tenantId, Guid contractId, RateCardSpec spec) =>
        new()
        {
            TenantId = tenantId,
            ContractId = contractId,
            OriginKind = spec.Origin.Kind,
            OriginState = spec.Origin.State,
            OriginCity = spec.Origin.City,
            OriginZone = spec.Origin.ZoneCode,
            DestinationKind = spec.Destination.Kind,
            DestinationState = spec.Destination.State,
            DestinationCity = spec.Destination.City,
            DestinationZone = spec.Destination.ZoneCode,
            BothWays = spec.BothWays,
            VehicleTypeId = spec.VehicleTypeId,
            MinDistanceKm = spec.MinDistanceKm,
            MaxDistanceKm = spec.MaxDistanceKm,
            Pricing = spec.Pricing,
        };

    public RateCardSpec ToSpec() => new(Origin, Destination, BothWays, VehicleTypeId, MinDistanceKm, MaxDistanceKm, Pricing);

    /// <summary>A canonical key used to spot two rates that would both apply to the same shipments.</summary>
    internal static string KeyOf(RateCardSpec s) =>
        $"{s.Origin}|{s.Destination}|{s.BothWays}|{s.VehicleTypeId}|{s.MinDistanceKm}|{s.MaxDistanceKm}";

    /// <summary>
    /// Whether this rate applies to the shipment, and how specifically (higher wins). A both-ways rate is also tried
    /// with origin and destination swapped.
    /// </summary>
    public bool Applies(FreightQuery q, Func<string, Zone?> zones, out int score)
    {
        score = 0;
        if (VehicleTypeId is { } vehicle && q.VehicleTypeId != vehicle)
        {
            return false;
        }

        if (HasDistanceBand)
        {
            if (q.DistanceKm is not { } km || km < (MinDistanceKm ?? 0m) || (MaxDistanceKm is { } max && km > max))
            {
                return false;
            }
        }

        var origin = Origin;
        var destination = Destination;
        var forward = origin.Matches(q.Origin, zones) && destination.Matches(q.Destination, zones);
        var reverse = BothWays && origin.Matches(q.Destination, zones) && destination.Matches(q.Origin, zones);
        if (!forward && !reverse)
        {
            return false;
        }

        score = ((origin.Specificity + destination.Specificity) * 100) + (VehicleTypeId is null ? 0 : 10) + (HasDistanceBand ? 1 : 0);
        return true;
    }
}
