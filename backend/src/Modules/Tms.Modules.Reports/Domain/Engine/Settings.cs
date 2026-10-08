using Tms.SharedKernel.India;

namespace Tms.Modules.Reports.Domain;

/// <summary>
/// Every threshold and choice the reports use. Nothing here is a business rule another module owns (a transporter's score weights, a POD SLA):
/// those are consumed from the module that owns them. Defaults apply until an administrator changes them.
/// </summary>
public sealed record ReportSettings
{
    /// <summary>Live (the five modules) or Demo (the built-in demonstration dataset).</summary>
    public string DataSource { get; init; } = "Live";

    /// <summary>Upper bound in days of each ageing bucket; the last bucket is "more than" the last bound.</summary>
    public int[] AgeingBuckets { get; init; } = [1, 3, 7, 15, 30];

    public bool AgeingUsesWorkingDays { get; init; }

    /// <summary>Working weekdays (0 = Sunday … 6 = Saturday). Default Monday to Saturday.</summary>
    public int[] WorkingDays { get; init; } = [1, 2, 3, 4, 5, 6];

    public string[] Holidays { get; init; } = [];

    public int OtpGraceMinutes { get; init; }

    public int OtdGraceMinutes { get; init; }

    /// <summary>An ETA is accurate when the last estimate was within this many minutes of the actual arrival.</summary>
    public int EtaToleranceMinutes { get; init; } = 30;

    /// <summary>How the middle is found when carriers are classed as low or high cost / performance: Median or Average.</summary>
    public string ClassificationMethod { get; init; } = "Median";

    public int DefaultPeriodDays { get; init; } = 30;

    public int TopN { get; init; } = 10;

    /// <summary>An export of at most this many rows is produced while the user waits; a bigger one becomes a background job.</summary>
    public int SyncExportRows { get; init; } = 5_000;

    public int ExportMaxRows { get; init; } = 500_000;

    public int JobKeepHours { get; init; } = 72;

    public int SlowReportMs { get; init; } = 3_000;

    public int DashboardCacheSeconds { get; init; } = 120;

    public int MetadataCacheSeconds { get; init; } = 300;

    public int ControlTowerCacheSeconds { get; init; } = 15;

    public int MaxPageSize { get; init; } = 500;

    public int ScheduleStopAfterFailures { get; init; } = 3;

    public int MaxSubscriptionsPerUser { get; init; } = 25;

    public int[] ExpiryBands { get; init; } = [90, 60, 30, 15, 7];

    /// <summary>State or city → region, overriding the built-in map.</summary>
    public Dictionary<string, string> RegionMap { get; init; } = [];

    public string Region(string? state) => state is not null && RegionMap.TryGetValue(state, out var r) ? r : IndiaRegions.OfState(state);

    public string RegionOf(string? city, string? state)
    {
        if (state is not null)
        {
            return Region(state);
        }

        if (city is null)
        {
            return IndiaRegions.Unknown;
        }

        if (RegionMap.TryGetValue(city, out var overridden))
        {
            return overridden;
        }

        return IndiaRegions.OfCity(city) is var r && r != IndiaRegions.Unknown ? Region(IndiaRegions.StateOfCity(city)) : IndiaRegions.Unknown;
    }

    public BusinessCalendar Calendar => new(WorkingDays, Holidays.Select(h => DateOnly.TryParse(h, out var d) ? d : (DateOnly?)null).Where(d => d is not null).Select(d => d!.Value).ToHashSet());
}

/// <summary>Working days and holidays, so an age or an SLA can be counted in the days a business actually works.</summary>
public sealed class BusinessCalendar(IReadOnlyCollection<int> workingDays, IReadOnlySet<DateOnly> holidays)
{
    public bool IsWorkingDay(DateOnly date) => workingDays.Contains((int)date.DayOfWeek) && !holidays.Contains(date);

    /// <summary>Whole working days from <paramref name="from"/> to <paramref name="to"/> (the day of <paramref name="from"/> is not counted). Never negative.</summary>
    public int WorkingDaysBetween(DateOnly from, DateOnly to)
    {
        var days = 0;
        for (var d = from.AddDays(1); d <= to; d = d.AddDays(1))
        {
            if (IsWorkingDay(d))
            {
                days++;
            }
        }

        return days;
    }

    public int DaysBetween(DateOnly from, DateOnly to, bool workingOnly) => to <= from ? 0 : workingOnly ? WorkingDaysBetween(from, to) : to.DayNumber - from.DayNumber;
}

/// <summary>Who is asking and what they may see. Built from the signed-in user, or frozen into a job or subscription so a later run is held to the same limits.</summary>
public sealed record ReportPrincipal(
    Guid UserId,
    Guid TenantId,
    IReadOnlySet<string> Permissions,
    Guid? TransporterId,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Scopes)
{
    public bool Has(string permission) => Permissions.Contains(permission);

    public bool IsExternal => TransporterId is not null;

    public IReadOnlyList<string> ScopeOf(string dimension) => Scopes.TryGetValue(dimension, out var v) ? v : [];

    public bool IsScopedOn(string dimension) => ScopeOf(dimension).Count > 0;

    /// <summary>Same limits, as a cache key part.</summary>
    public string ScopeKey => $"{TransporterId}|" + string.Join(';', Scopes.OrderBy(s => s.Key, StringComparer.Ordinal).Select(s => $"{s.Key}={string.Join(',', s.Value.Order())}"));
}
