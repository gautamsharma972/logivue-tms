using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Domain;

public enum MilkRunStopType
{
    /// <summary>Collects goods (a supplier) and brings them to the depot.</summary>
    Pickup = 1,

    /// <summary>Delivers goods carried from the depot (a customer or store).</summary>
    Delivery = 2,
}

public sealed record MilkRunStopSpec(Guid LocationId, MilkRunStopType Type, int ServiceMinutes, TimeOnly? WindowFrom, TimeOnly? WindowTo);

/// <summary>One stop on a template's planned route.</summary>
public sealed class MilkRunStop : Entity, ITenantScoped
{
    private MilkRunStop()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid MilkRunTemplateId { get; private set; }

    /// <summary>1-based position in the planned sequence.</summary>
    public int Sequence { get; private set; }

    public Guid LocationId { get; private set; }

    public MilkRunStopType Type { get; private set; }

    public int ServiceMinutes { get; private set; }

    public TimeOnly? WindowFrom { get; private set; }

    public TimeOnly? WindowTo { get; private set; }

    internal static MilkRunStop Create(Guid tenantId, Guid templateId, int sequence, MilkRunStopSpec spec) => new()
    {
        TenantId = tenantId, MilkRunTemplateId = templateId, Sequence = sequence, LocationId = spec.LocationId, Type = spec.Type,
        ServiceMinutes = spec.ServiceMinutes, WindowFrom = spec.WindowFrom, WindowTo = spec.WindowTo,
    };
}

/// <summary>
/// A reusable route: a depot and an ordered set of pickup and delivery points that is run on certain weekdays. The template holds
/// the planned sequence and limits; what actually travels, in which vehicle, and what it costs is worked out for each day.
/// </summary>
public sealed class MilkRunTemplate : AggregateRoot, ITenantScoped
{
    public const int MaxTemplateStops = 50;

    private readonly List<MilkRunStop> _stops = [];

    private MilkRunTemplate()
    {
    }

    public Guid TenantId { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public Guid DepotLocationId { get; private set; }

    /// <summary>The vehicle usually used. Each day's plan may choose another if the load or price says so.</summary>
    public Guid? VehicleTypeId { get; private set; }

    /// <summary>Most stops one vehicle may serve in a day; a bigger day is split into several trips.</summary>
    public int MaxStops { get; private set; }

    public int MaxDurationMinutes { get; private set; }

    public TimeOnly DepartureTime { get; private set; }

    public IReadOnlyList<DayOfWeek> Days { get; private set; } = [];

    public bool IsActive { get; private set; } = true;

    public IReadOnlyCollection<MilkRunStop> Stops => _stops;

    public IReadOnlyList<MilkRunStop> StopsInSequence => _stops.OrderBy(s => s.Sequence).ToList();

    public static Result<MilkRunTemplate> Create(
        Guid tenantId, string code, string name, Guid depotLocationId, Guid? vehicleTypeId, int maxStops, int maxDurationMinutes,
        TimeOnly departureTime, IReadOnlyList<DayOfWeek> days, IReadOnlyList<MilkRunStopSpec> stops)
    {
        var template = new MilkRunTemplate { TenantId = tenantId };
        var set = template.Set(code, name, depotLocationId, vehicleTypeId, maxStops, maxDurationMinutes, departureTime, days, stops, true);
        return set.IsFailure ? set.Error : template;
    }

    public Result Update(
        string code, string name, Guid depotLocationId, Guid? vehicleTypeId, int maxStops, int maxDurationMinutes,
        TimeOnly departureTime, IReadOnlyList<DayOfWeek> days, IReadOnlyList<MilkRunStopSpec> stops, bool isActive) =>
        Set(code, name, depotLocationId, vehicleTypeId, maxStops, maxDurationMinutes, departureTime, days, stops, isActive);

    private Result Set(
        string code, string name, Guid depotLocationId, Guid? vehicleTypeId, int maxStops, int maxDurationMinutes,
        TimeOnly departureTime, IReadOnlyList<DayOfWeek> days, IReadOnlyList<MilkRunStopSpec> stops, bool isActive)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 30 || !System.Text.RegularExpressions.Regex.IsMatch(code.Trim(), "^[A-Za-z0-9][A-Za-z0-9._-]*$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromMilliseconds(100)))
        {
            errors["code"] = ["Use up to 30 letters, digits, '.', '_' or '-'."];
        }

        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
        {
            errors["name"] = ["Name is required (up to 200 characters)."];
        }

        if (depotLocationId == Guid.Empty)
        {
            errors["depotLocationId"] = ["Choose the depot."];
        }

        if (maxStops is < 1 or > MaxTemplateStops)
        {
            errors["maxStops"] = [$"Maximum stops must be between 1 and {MaxTemplateStops}."];
        }

        if (maxDurationMinutes is < 30 or > 1440)
        {
            errors["maxDurationMinutes"] = ["Maximum duration must be between 30 minutes and 24 hours."];
        }

        if (days.Count == 0 || days.Distinct().Count() != days.Count || days.Any(d => !Enum.IsDefined(d)))
        {
            errors["days"] = ["Choose the weekdays this run operates, each once."];
        }

        if (stops.Count is < 1 or > MaxTemplateStops)
        {
            errors["stops"] = [$"A milk run needs between 1 and {MaxTemplateStops} stops."];
        }
        else
        {
            if (stops.Select(s => s.LocationId).Distinct().Count() != stops.Count)
            {
                errors["stops"] = ["Each location can appear once."];
            }
            else if (stops.Any(s => s.LocationId == depotLocationId))
            {
                errors["stops"] = ["The depot is where the run starts and ends; it is not also a stop."];
            }
            else if (stops.Any(s => !Enum.IsDefined(s.Type) || s.ServiceMinutes is < 0 or > 480))
            {
                errors["stops"] = ["Each stop needs a type and 0–480 minutes of service time."];
            }
            else if (stops.Any(s => s.WindowFrom is null != s.WindowTo is null || (s.WindowFrom is { } f && s.WindowTo is { } t && t <= f)))
            {
                errors["stops"] = ["A stop window needs both times, with closing after opening."];
            }
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        DepotLocationId = depotLocationId;
        VehicleTypeId = vehicleTypeId;
        MaxStops = maxStops;
        MaxDurationMinutes = maxDurationMinutes;
        DepartureTime = departureTime;
        Days = days.Order().ToList();
        IsActive = isActive;

        _stops.Clear();
        for (var i = 0; i < stops.Count; i++)
        {
            _stops.Add(MilkRunStop.Create(TenantId, Id, i + 1, stops[i]));
        }

        return Result.Success();
    }
}
