using System.Globalization;

namespace Tms.Modules.Reports.Domain;

/// <summary>When a scheduled report runs: the time of day and, by schedule type, the weekday (Weekly), the day of the month (Monthly; 0 = the last day) or the interval in minutes (Custom).</summary>
public sealed record ScheduleDefinition(string? Time = "08:00", string? DayOfWeek = null, int? DayOfMonth = null, int? EveryMinutes = null);

/// <summary>Works out when a schedule is next due, in the schedule's own time zone, so "08:00" is 08:00 for the person who set it whatever the server's clock says.</summary>
public static class ScheduleCalculator
{
    public const int MinimumIntervalMinutes = 15;

    public static string? Validate(ScheduleType type, ScheduleDefinition d, string timeZone)
    {
        if (ZoneOrNull(timeZone) is null)
        {
            return $"'{timeZone}' is not a time zone this server knows.";
        }

        if (type != ScheduleType.Custom && !TimeOnly.TryParseExact(d.Time ?? string.Empty, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            return "Give the time of day as HH:mm, for example 08:00.";
        }

        return type switch
        {
            ScheduleType.Weekly when !Enum.TryParse<DayOfWeek>(d.DayOfWeek, ignoreCase: true, out _) => "Choose the weekday (Monday to Sunday).",
            ScheduleType.Monthly when d.DayOfMonth is null or < 0 or > 31 => "Choose the day of the month (1 to 31, or 0 for the last day).",
            ScheduleType.Custom when d.EveryMinutes is null or < MinimumIntervalMinutes or > 10_080 => $"Choose an interval of {MinimumIntervalMinutes} minutes to 7 days.",
            _ => null,
        };
    }

    public static TimeZoneInfo? ZoneOrNull(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>The first run strictly after <paramref name="after"/>.</summary>
    public static DateTimeOffset Next(ScheduleType type, ScheduleDefinition d, string timeZone, DateTimeOffset after)
    {
        var zone = ZoneOrNull(timeZone) ?? TimeZoneInfo.Utc;
        if (type == ScheduleType.Custom)
        {
            return after.AddMinutes(Math.Max(MinimumIntervalMinutes, d.EveryMinutes ?? MinimumIntervalMinutes));
        }

        var time = TimeOnly.TryParseExact(d.Time ?? "08:00", "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : new TimeOnly(8, 0);
        var localNow = TimeZoneInfo.ConvertTime(after, zone);
        var day = DateOnly.FromDateTime(localNow.DateTime);
        for (var i = 0; i < 400; i++)
        {
            var candidate = day.AddDays(i);
            var matches = type switch
            {
                ScheduleType.Weekly => candidate.DayOfWeek == (Enum.TryParse<DayOfWeek>(d.DayOfWeek, ignoreCase: true, out var dow) ? dow : System.DayOfWeek.Monday),
                ScheduleType.Monthly => candidate.Day == MonthDay(candidate, d.DayOfMonth ?? 1),
                _ => true,
            };
            if (!matches)
            {
                continue;
            }

            var local = candidate.ToDateTime(time);
            if (zone.IsInvalidTime(local))
            {
                local = local.AddHours(1);
            }

            var utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
            if (utc > after)
            {
                return utc;
            }
        }

        return after.AddDays(1);
    }

    private static int MonthDay(DateOnly inMonth, int wanted)
    {
        var last = DateTime.DaysInMonth(inMonth.Year, inMonth.Month);
        return wanted == 0 ? last : Math.Min(wanted, last);
    }
}
