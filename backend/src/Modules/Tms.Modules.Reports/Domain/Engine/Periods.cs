namespace Tms.Modules.Reports.Domain;

public enum PeriodKind { Custom, Daily, Weekly, Monthly, Quarterly, Ytd, Rolling30, Rolling90 }

public enum CompareMode { None, PreviousPeriod, SamePeriodLastYear }

public readonly record struct DateRange(DateOnly From, DateOnly To)
{
    public int Days => To.DayNumber - From.DayNumber + 1;

    public bool Contains(DateOnly d) => d >= From && d <= To;
}

/// <summary>Turns a period choice into dates, and finds the period to compare it with.</summary>
public static class Periods
{
    public static PeriodKind Parse(string? text) => Enum.TryParse<PeriodKind>(text?.Replace("-", string.Empty, StringComparison.Ordinal), ignoreCase: true, out var kind) ? kind : PeriodKind.Custom;

    /// <summary>The period of the given kind that contains <paramref name="today"/>; for Custom, the dates given.</summary>
    public static DateRange Resolve(PeriodKind kind, DateOnly today, DateOnly? from, DateOnly? to, int defaultDays) => kind switch
    {
        PeriodKind.Daily => new(today, today),
        PeriodKind.Weekly => WeekOf(today),
        PeriodKind.Monthly => new(new DateOnly(today.Year, today.Month, 1), new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1)),
        PeriodKind.Quarterly => QuarterOf(today),
        PeriodKind.Ytd => new(new DateOnly(today.Year, 1, 1), today),
        PeriodKind.Rolling30 => new(today.AddDays(-29), today),
        PeriodKind.Rolling90 => new(today.AddDays(-89), today),
        _ => from is { } f && to is { } t ? new(f <= t ? f : t, f <= t ? t : f) : new(today.AddDays(-(defaultDays - 1)), today),
    };

    public static DateRange WeekOf(DateOnly day)
    {
        var offset = ((int)day.DayOfWeek + 6) % 7; // Monday first
        var start = day.AddDays(-offset);
        return new(start, start.AddDays(6));
    }

    public static DateRange QuarterOf(DateOnly day)
    {
        var first = new DateOnly(day.Year, ((day.Month - 1) / 3 * 3) + 1, 1);
        return new(first, first.AddMonths(3).AddDays(-1));
    }

    /// <summary>The equally long period just before, or the same dates a year earlier. Month, quarter and year-to-date periods compare whole months/quarters.</summary>
    public static DateRange Compare(DateRange range, CompareMode mode, PeriodKind kind)
    {
        if (mode == CompareMode.SamePeriodLastYear)
        {
            return new(range.From.AddYears(-1), range.To.AddYears(-1));
        }

        return kind switch
        {
            PeriodKind.Monthly => new(range.From.AddMonths(-1), range.From.AddDays(-1)),
            PeriodKind.Quarterly => new(range.From.AddMonths(-3), range.From.AddDays(-1)),
            PeriodKind.Ytd => new(range.From.AddYears(-1), range.To.AddYears(-1)),
            _ => new(range.From.AddDays(-range.Days), range.From.AddDays(-1)),
        };
    }

    public static string Week(DateOnly d) => $"{System.Globalization.ISOWeek.GetYear(d.ToDateTime(TimeOnly.MinValue))}-W{System.Globalization.ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue)):00}";

    public static string Month(DateOnly d) => $"{d.Year}-{d.Month:00}";

    public static string Quarter(DateOnly d) => $"{d.Year}-Q{((d.Month - 1) / 3) + 1}";

    public static string Bucket(DateOnly d, string grain) => grain.ToLowerInvariant() switch
    {
        "day" or "date" or "daily" => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        "week" or "weekly" => Week(d),
        "quarter" or "quarterly" => Quarter(d),
        "year" or "yearly" => d.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Month(d),
    };
}
