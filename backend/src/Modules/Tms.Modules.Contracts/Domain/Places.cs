using System.Text.Json.Serialization;
using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Contracts.Domain;

/// <summary>How precisely a rate names a place. Higher is more specific and wins when several rates match.</summary>
public enum PlaceKind
{
    /// <summary>Matches anywhere (catch-all, e.g. an all-India rate by distance band).</summary>
    Any = 0,
    State = 1,
    Zone = 2,
    City = 3,
}

/// <summary>A concrete address component of a shipment: where a load is picked up or delivered.</summary>
public sealed record Location(string State, string? City = null, string? Pincode = null)
{
    public string NormalState => Text.Normalise(State);

    public string? NormalCity => string.IsNullOrWhiteSpace(City) ? null : Text.Normalise(City);
}

/// <summary>One end of a lane as written in a rate card.</summary>
public sealed record Place
{
    [JsonConstructor]
    private Place(PlaceKind kind, string? state, string? city, string? zoneCode) =>
        (Kind, State, City, ZoneCode) = (kind, state, city, zoneCode);

    public PlaceKind Kind { get; }

    public string? State { get; }

    public string? City { get; }

    public string? ZoneCode { get; }

    public static Place Anywhere { get; } = new(PlaceKind.Any, null, null, null);

    public static Place OfState(string state) => new(PlaceKind.State, Text.Normalise(state), null, null);

    public static Place OfCity(string state, string city) => new(PlaceKind.City, Text.Normalise(state), Text.Normalise(city), null);

    public static Place OfZone(string zoneCode) => new(PlaceKind.Zone, null, null, Text.Normalise(zoneCode));

    /// <summary>Rebuilds a place from stored columns, validating that the parts fit the kind.</summary>
    public static Result<Place> From(PlaceKind kind, string? state, string? city, string? zoneCode) => kind switch
    {
        PlaceKind.Any => Anywhere,
        PlaceKind.State when !string.IsNullOrWhiteSpace(state) => OfState(state),
        PlaceKind.City when !string.IsNullOrWhiteSpace(state) && !string.IsNullOrWhiteSpace(city) => OfCity(state, city),
        PlaceKind.Zone when !string.IsNullOrWhiteSpace(zoneCode) => OfZone(zoneCode),
        _ => Error.Validation("contracts.place_invalid", $"A {kind} place needs {kind switch { PlaceKind.City => "a state and a city", PlaceKind.State => "a state", PlaceKind.Zone => "a zone code", _ => "no details" }}."),
    };

    [JsonIgnore]
    public int Specificity => (int)Kind;

    /// <param name="zones">Resolves a zone code to its definition (null if the zone does not exist).</param>
    public bool Matches(Location location, Func<string, Zone?> zones) => Kind switch
    {
        PlaceKind.Any => true,
        PlaceKind.State => State == location.NormalState,
        PlaceKind.City => State == location.NormalState && City == location.NormalCity,
        PlaceKind.Zone => ZoneCode is { } code && zones(code)?.Contains(location) == true,
        _ => false,
    };

    public override string ToString() => Kind switch
    {
        PlaceKind.Any => "Anywhere",
        PlaceKind.State => State!,
        PlaceKind.City => $"{City}, {State}",
        PlaceKind.Zone => $"Zone {ZoneCode}",
        _ => string.Empty,
    };
}

internal static class Text
{
    /// <summary>Upper-case, trimmed, inner whitespace collapsed: "  pune  " and "Pune" are the same city.</summary>
    public static string Normalise(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToUpperInvariant();
}

/// <param name="City">Null means the whole state.</param>
public sealed record ZoneMember(string State, string? City);

/// <summary>A named group of places (e.g. "WEST-1": Maharashtra and Gujarat) that rate cards can refer to.</summary>
public sealed class Zone : AggregateRoot, ITenantScoped
{
    public const int MaxMembers = 500;

    private Zone()
    {
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public IReadOnlyList<ZoneMember> Members { get; private set; } = [];

    public static Result<Zone> Create(Guid tenantId, string code, string name, IEnumerable<ZoneMember> members)
    {
        var zone = new Zone { TenantId = tenantId, Code = Text.Normalise(code ?? string.Empty).Replace(' ', '_') };
        if (zone.Code.Length is 0 or > 32 || !zone.Code.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            return Error.Validation("zones.code_invalid", "Use up to 32 letters, digits, '-' or '_' for the zone code.");
        }

        var result = zone.Update(name, members);
        return result.IsFailure ? result.Error : zone;
    }

    public Result Update(string name, IEnumerable<ZoneMember> members)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("zones.name_invalid", "A zone name of up to 100 characters is required.");
        }

        var normalised = members
            .Where(m => !string.IsNullOrWhiteSpace(m.State))
            .Select(m => new ZoneMember(Text.Normalise(m.State), string.IsNullOrWhiteSpace(m.City) ? null : Text.Normalise(m.City)))
            .Distinct()
            .ToList();

        if (normalised.Count is 0 or > MaxMembers)
        {
            return Error.Validation("zones.members_invalid", $"A zone needs between 1 and {MaxMembers} places.");
        }

        Name = name.Trim();
        Members = normalised;
        return Result.Success();
    }

    public bool Contains(Location location) =>
        Members.Any(m => m.State == location.NormalState && (m.City is null || m.City == location.NormalCity));
}
