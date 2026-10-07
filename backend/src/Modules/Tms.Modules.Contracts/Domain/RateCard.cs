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
    Pricing Pricing,
    RateExtras? Extras = null);

/// <summary>
/// What a rate adds to its lane and pricing: a stable code and version, a preference, caps and floors, weight and volume bands that decide which shipments it is for,
/// its own validity inside the contract's, the capabilities a shipment must need, and the DPH rule that adjusts it.
/// </summary>
public sealed record RateExtras(
    string? Code = null,
    int Priority = RateExtras.DefaultPriority,
    decimal? MinimumCharge = null,
    decimal? MaximumCharge = null,
    decimal? MinWeightKg = null,
    decimal? MaxWeightKg = null,
    decimal? MinVolumeCbm = null,
    decimal? MaxVolumeCbm = null,
    DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null,
    IReadOnlyList<string>? RequiredCapabilities = null,
    string? DphRuleCode = null,
    string? Notes = null)
{
    /// <summary>Lower numbers are preferred. 100 is the ordinary rate.</summary>
    public const int DefaultPriority = 100;

    public static RateExtras None { get; } = new();

    public Result Validate()
    {
        static Result Fail(string field, string message) =>
            Error.Validation("contracts.rate_extras_invalid", message) with { ValidationErrors = new Dictionary<string, string[]> { [field] = [message] } };

        if (Priority is < 1 or > 999)
        {
            return Fail("priority", "Priority must be between 1 (most preferred) and 999.");
        }

        if (MinimumCharge is < 0 || MaximumCharge is < 0 || (MinimumCharge is { } lo && MaximumCharge is { } hi && hi < lo))
        {
            return Fail("maximumCharge", "The maximum charge cannot be below the minimum, and neither can be negative.");
        }

        if (Band(MinWeightKg, MaxWeightKg) is { } w)
        {
            return Fail("minWeightKg", $"The weight band is invalid: {w}");
        }

        if (Band(MinVolumeCbm, MaxVolumeCbm) is { } v)
        {
            return Fail("minVolumeCbm", $"The volume band is invalid: {v}");
        }

        if (ValidFrom is { } from && ValidTo is { } to && to < from)
        {
            return Fail("validTo", "The rate must end on or after the day it starts.");
        }

        if (Code is { Length: > 40 } || DphRuleCode is { Length: > 40 } || Notes is { Length: > 500 })
        {
            return Fail("code", "Codes can be at most 40 characters and notes 500.");
        }

        return RequiredCapabilities?.Any(c => string.IsNullOrWhiteSpace(c) || c.Length > 40) == true ? Fail("requiredCapabilities", "A capability is blank or too long.") : Result.Success();
    }

    private static string? Band(decimal? from, decimal? to) =>
        from < 0 || to < 0 ? "negative"
        : from is { } lo && to is { } hi && hi <= lo ? "the upper limit must be above the lower limit"
        : null;
}

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

    /// <summary>Stable across revisions (the rate "RT-AB12CD34" stays that rate); <see cref="Version"/> counts the times its terms changed.</summary>
    public string Code { get; private set; } = null!;

    public int Version { get; private set; } = 1;

    public int Priority { get; private set; } = RateExtras.DefaultPriority;

    public decimal? MinimumCharge { get; private set; }

    public decimal? MaximumCharge { get; private set; }

    public decimal? MinWeightKg { get; private set; }

    public decimal? MaxWeightKg { get; private set; }

    public decimal? MinVolumeCbm { get; private set; }

    public decimal? MaxVolumeCbm { get; private set; }

    public DateOnly? ValidFrom { get; private set; }

    public DateOnly? ValidTo { get; private set; }

    public IReadOnlyList<string> RequiredCapabilities { get; private set; } = [];

    public string? DphRuleCode { get; private set; }

    public string? Notes { get; private set; }

    public Place Origin => Place.From(OriginKind, OriginState, OriginCity, OriginZone).Value;

    public Place Destination => Place.From(DestinationKind, DestinationState, DestinationCity, DestinationZone).Value;

    public bool HasDistanceBand => MinDistanceKm is not null || MaxDistanceKm is not null;

    public bool HasWeightBand => MinWeightKg is not null || MaxWeightKg is not null;

    public bool HasVolumeBand => MinVolumeCbm is not null || MaxVolumeCbm is not null;

    internal static RateCard Create(Guid tenantId, Guid contractId, RateCardSpec spec, int version = 1)
    {
        var x = spec.Extras ?? RateExtras.None;
        return new()
        {
            Code = string.IsNullOrWhiteSpace(x.Code) ? AutoCode(spec) : x.Code.Trim().ToUpperInvariant(),
            Version = version,
            Priority = x.Priority,
            MinimumCharge = x.MinimumCharge,
            MaximumCharge = x.MaximumCharge,
            MinWeightKg = x.MinWeightKg,
            MaxWeightKg = x.MaxWeightKg,
            MinVolumeCbm = x.MinVolumeCbm,
            MaxVolumeCbm = x.MaxVolumeCbm,
            ValidFrom = x.ValidFrom,
            ValidTo = x.ValidTo,
            RequiredCapabilities = x.RequiredCapabilities?.Select(c => c.Trim().ToUpperInvariant()).Distinct().Order().ToList() ?? [],
            DphRuleCode = string.IsNullOrWhiteSpace(x.DphRuleCode) ? null : x.DphRuleCode.Trim().ToUpperInvariant(),
            Notes = x.Notes,
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
    }

    public RateExtras Extras => new(Code, Priority, MinimumCharge, MaximumCharge, MinWeightKg, MaxWeightKg, MinVolumeCbm, MaxVolumeCbm, ValidFrom, ValidTo, RequiredCapabilities, DphRuleCode, Notes);

    public RateCardSpec ToSpec() => new(Origin, Destination, BothWays, VehicleTypeId, MinDistanceKm, MaxDistanceKm, Pricing, Extras);

    /// <summary>A readable, stable code for a rate that was given none: its lane, vehicle and band, hashed so it is short.</summary>
    internal static string AutoCode(RateCardSpec s)
    {
        var x = s.Extras ?? RateExtras.None;
        var key = $"{KeyOf(s)}|{s.Pricing.ServiceType()}|{x.MinWeightKg}|{x.MaxWeightKg}|{x.MinVolumeCbm}|{x.MaxVolumeCbm}";
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        return $"RT-{Convert.ToHexString(hash.AsSpan(0, 4))}";
    }

    /// <summary>A canonical key used to spot two rates that would both apply to the same shipments.</summary>
    internal static string KeyOf(RateCardSpec s) =>
        $"{s.Origin}|{s.Destination}|{s.BothWays}|{s.VehicleTypeId}|{s.MinDistanceKm}|{s.MaxDistanceKm}|{s.Extras?.MinWeightKg}|{s.Extras?.MaxWeightKg}|{s.Extras?.MinVolumeCbm}|{s.Extras?.MaxVolumeCbm}|{s.Extras?.ValidFrom}|{s.Extras?.ValidTo}|{s.Extras?.Priority}|{s.Pricing.ServiceType()}|{string.Join(",", (s.Extras?.RequiredCapabilities ?? []).Select(c => c.Trim().ToUpperInvariant()).Order())}";

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

        if (ValidFrom is { } from && q.Date < from || ValidTo is { } to && q.Date > to)
        {
            return false;
        }

        if (HasWeightBand && (q.WeightKg is not { } kg || kg < (MinWeightKg ?? 0m) || (MaxWeightKg is { } maxKg && kg > maxKg)))
        {
            return false;
        }

        if (HasVolumeBand && (q.VolumeCbm is not { } cbm || cbm < (MinVolumeCbm ?? 0m) || (MaxVolumeCbm is { } maxCbm && cbm > maxCbm)))
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
