using System.Globalization;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Reports.Domain;

/// <summary>Small helpers every report definition shares: column and filter shorthand and the few calculations that are only arithmetic on listed rows.</summary>
internal static class Kit
{
    public const string CatExecutive = "Executive Dashboard";
    public const string CatPlanning = "Planning & Load Optimisation";
    public const string CatTransporters = "Transporter Management";
    public const string CatPod = "POD & Delivery";
    public const string CatTracking = "Shipment Tracking & Visibility";
    public const string CatContracts = "Freight Contract Management";
    public const string CatCross = "Cross-Module Analytics";

    public static readonly string[] Reasons = ["NoVehicle", "PayloadExceeded", "VolumeExceeded", "SlaImpossible", "NoCompatibleVehicle", "NoRate", "NoRoute", "LockedConflict", "Other"];

    public static ColumnSpec C(string field, string display, FieldType type = FieldType.Text, bool filter = false, string? format = null, bool visible = true, bool sortable = true) =>
        new(field, display, type, 0, sortable, filter, format, visible);

    public static IReadOnlyList<ColumnSpec> Cols(params ColumnSpec[] columns) => columns.Select((c, i) => c with { Sequence = i + 1 }).ToList();

    private static readonly Dictionary<string, (FieldType Type, string? Lookup)> FilterKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        [FilterNames.FromDate] = (FieldType.Date, null),
        [FilterNames.ToDate] = (FieldType.Date, null),
        [FilterNames.Period] = (FieldType.Text, "period"),
        [FilterNames.Compare] = (FieldType.Text, "compare"),
        [FilterNames.Transporter] = (FieldType.Text, "transporter"),
        [FilterNames.Lane] = (FieldType.Text, "lane"),
        [FilterNames.Region] = (FieldType.Text, "region"),
        [FilterNames.Customer] = (FieldType.Text, "customer"),
        [FilterNames.VehicleType] = (FieldType.Text, "vehicleType"),
        [FilterNames.ServiceType] = (FieldType.Text, "service"),
        [FilterNames.Origin] = (FieldType.Text, "city"),
        [FilterNames.Destination] = (FieldType.Text, "city"),
        [FilterNames.Zone] = (FieldType.Text, "zone"),
        [FilterNames.BusinessUnit] = (FieldType.Text, "businessUnit"),
        [FilterNames.Vehicle] = (FieldType.Text, null),
        [FilterNames.Driver] = (FieldType.Text, null),
        [FilterNames.Shipment] = (FieldType.Text, null),
        [FilterNames.Load] = (FieldType.Text, null),
        [FilterNames.Trip] = (FieldType.Text, null),
        [FilterNames.Contract] = (FieldType.Text, null),
        [FilterNames.Status] = (FieldType.Text, null),
        [FilterNames.Exception] = (FieldType.Text, null),
        [FilterNames.Search] = (FieldType.Text, null),
        ["severity"] = (FieldType.Text, "severity"),
        ["onTime"] = (FieldType.Text, "yesno"),
        ["clock"] = (FieldType.Text, "clock"),
        ["openOnly"] = (FieldType.Boolean, null),
        ["minAgeDays"] = (FieldType.Whole, null),
        ["rankBy"] = (FieldType.Text, "rankBy"),
        ["metric"] = (FieldType.Text, "metric"),
        ["grain"] = (FieldType.Text, "grain"),
        ["by"] = (FieldType.Text, "dimension"),
    };

    public static IReadOnlyList<FilterSpec> Filters(params string[] names) =>
        names.Select((n, i) => new FilterSpec(n, FilterKinds[n].Type, i + 1, false, null, FilterKinds[n].Lookup)).ToList();

    public static FilterSpec With(this FilterSpec f, string? lookup = null, string? @default = null) => f with { Lookup = lookup ?? f.Lookup, Default = @default ?? f.Default };

    public static IReadOnlyList<GroupingSpec> Groups(params (string Field, string Display)[] groups) => groups.Select((g, i) => new GroupingSpec(g.Field, g.Display, i + 1)).ToList();

    public static IReadOnlyList<SortSpec> Sort(string field, bool descending = false) => [new SortSpec(field, descending, 1)];

    public static IReadOnlyDictionary<string, string> Map(params (string Filter, string Field)[] pairs) => pairs.ToDictionary(p => p.Filter, p => p.Field);

    public static decimal? Pct(decimal numerator, decimal denominator) => denominator <= 0 ? null : Math.Round(numerator / denominator * 100m, 1);

    public static decimal? Div(decimal numerator, decimal denominator, int digits = 2) => denominator <= 0 ? null : Math.Round(numerator / denominator, digits);

    public static decimal? Avg(IEnumerable<decimal?> values)
    {
        var v = values.Where(x => x is not null).Select(x => x!.Value).ToList();
        return v.Count == 0 ? null : Math.Round(v.Average(), 2);
    }

    public static decimal Num(object? value) => Convert.ToDecimal(value, CultureInfo.InvariantCulture);

    public static int One(bool value) => value ? 1 : 0;

    public static string Bucket(DateOnly d, string grain) => Periods.Bucket(d, grain);

    public static string Yn(bool? v) => v switch { true => "Yes", false => "No", _ => "Not measurable" };

    public static decimal? Minutes(DateTimeOffset? later, DateTimeOffset? earlier) => later is { } l && earlier is { } e ? Math.Round((decimal)(l - e).TotalMinutes, 0) : null;

    public static DateOnly Day(DateTimeOffset t) => DateOnly.FromDateTime(t.UtcDateTime.AddMinutes(330));

    public static int AgeDays(DateTimeOffset from, DateTimeOffset to, ReportSettings s) => s.Calendar.DaysBetween(Day(from), Day(to), s.AgeingUsesWorkingDays);

    /// <summary>The ageing bucket label for an age in days, from the organisation's bucket bounds ("0–1 days", "2–3 days", … ">30 days").</summary>
    public static string AgeBucket(int days, int[] bounds)
    {
        var low = 0;
        foreach (var upper in bounds)
        {
            if (days <= upper)
            {
                return low == upper ? $"{upper} day" : $"{low}–{upper} days";
            }

            low = upper + 1;
        }

        return $">{bounds[^1]} days";
    }

    public static IReadOnlyList<string> AgeBuckets(int[] bounds)
    {
        var list = new List<string>();
        var low = 0;
        foreach (var upper in bounds)
        {
            list.Add(AgeBucket(upper, bounds));
            low = upper + 1;
        }

        _ = low;
        list.Add($">{bounds[^1]} days");
        return list;
    }

    public static string Money(decimal? v) => v is null ? "—" : v.Value.ToString("N0", CultureInfo.GetCultureInfo("en-IN"));

    /// <summary>Pearson correlation of two series; null when there are fewer than three pairs or one series does not vary.</summary>
    public static decimal? Correlation(IReadOnlyList<(decimal X, decimal Y)> pairs)
    {
        if (pairs.Count < 3)
        {
            return null;
        }

        var mx = pairs.Average(p => (double)p.X);
        var my = pairs.Average(p => (double)p.Y);
        var sxx = pairs.Sum(p => Math.Pow((double)p.X - mx, 2));
        var syy = pairs.Sum(p => Math.Pow((double)p.Y - my, 2));
        if (sxx == 0 || syy == 0)
        {
            return null;
        }

        return Math.Round((decimal)(pairs.Sum(p => ((double)p.X - mx) * ((double)p.Y - my)) / Math.Sqrt(sxx * syy)), 2);
    }

    public static decimal? Median(IEnumerable<decimal> values)
    {
        var v = values.Order().ToList();
        if (v.Count == 0)
        {
            return null;
        }

        return v.Count % 2 == 1 ? v[v.Count / 2] : (v[(v.Count / 2) - 1] + v[v.Count / 2]) / 2m;
    }

    public static decimal? Middle(IEnumerable<decimal> values, ReportSettings s) =>
        s.ClassificationMethod.Equals("Average", StringComparison.OrdinalIgnoreCase) ? (values.Any() ? values.Average() : null) : Median(values);

    public static ChartData Chart(string id, string title, string kind, string? x, IEnumerable<ReportRow> data, params ChartSeries[] series) =>
        new(id, title, kind, x, series, data.ToList());

    /// <summary>Per-value KPIs: for every value of a dimension (each carrier, each lane), the KPIs asked for, each calculated by the one KPI engine under that value's filter.</summary>
    public static async Task<Dictionary<string, Dictionary<string, KpiResult>>> KpisBy(ReportFacts facts, string filter, IEnumerable<string> values, IEnumerable<string> codes)
    {
        var result = new Dictionary<string, Dictionary<string, KpiResult>>(StringComparer.OrdinalIgnoreCase);
        var list = codes.ToList();
        foreach (var value in values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var scoped = facts.With(filter, value);
            var row = new Dictionary<string, KpiResult>();
            foreach (var code in list)
            {
                row[code] = await KpiCatalogue.CalculateAsync(KpiCatalogue.Find(code)!, scoped);
            }

            result[value] = row;
        }

        return result;
    }
}
