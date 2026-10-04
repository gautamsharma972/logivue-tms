using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Shipments.Domain;

/// <summary>Two product categories that must never share a vehicle (e.g. FOOD and CHEMICALS). Maintained per tenant.</summary>
public sealed class ProductCompatibilityRule : AggregateRoot, ITenantScoped
{
    private ProductCompatibilityRule()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>The lower of the two category names (ordinal), so a pair is stored once whichever way round it was entered.</summary>
    public string CategoryA { get; private set; } = null!;

    public string CategoryB { get; private set; } = null!;

    public string? Reason { get; private set; }

    public static Result<ProductCompatibilityRule> Create(Guid tenantId, string categoryA, string categoryB, string? reason)
    {
        var a = categoryA?.Trim().ToUpperInvariant() ?? string.Empty;
        var b = categoryB?.Trim().ToUpperInvariant() ?? string.Empty;
        var errors = new Dictionary<string, string[]>();
        if (a.Length is 0 or > 50)
        {
            errors["categoryA"] = ["Enter the first category (up to 50 characters)."];
        }

        if (b.Length is 0 or > 50)
        {
            errors["categoryB"] = ["Enter the second category (up to 50 characters)."];
        }

        if (a == b && a.Length > 0)
        {
            errors["categoryB"] = ["Choose two different categories."];
        }

        if (reason?.Trim().Length > 300)
        {
            errors["reason"] = ["The reason can be at most 300 characters."];
        }

        if (errors.Count > 0)
        {
            return Error.Validation("validation.failed", "One or more fields are invalid.") with { ValidationErrors = errors };
        }

        var ordered = string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
        return new ProductCompatibilityRule
        {
            TenantId = tenantId, CategoryA = ordered.Item1, CategoryB = ordered.Item2, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
        };
    }
}

public sealed record CompatibilityPair(string CategoryA, string CategoryB, string? Reason);

/// <summary>Decides whether orders may share a vehicle: tenant rules for category pairs, and optionally dangerous goods apart from the rest.</summary>
public sealed class CompatibilityPolicy(IEnumerable<CompatibilityPair> pairs, bool separateHazardous)
{
    private readonly HashSet<(string, string)> _blocked = pairs.Select(p => Key(p.CategoryA, p.CategoryB)).ToHashSet();
    private readonly Dictionary<(string, string), string?> _reasons = pairs.GroupBy(p => Key(p.CategoryA, p.CategoryB)).ToDictionary(g => g.Key, g => g.First().Reason);

    public static CompatibilityPolicy None { get; } = new([], false);

    private static (string, string) Key(string a, string b)
    {
        a = a.ToUpperInvariant();
        b = b.ToUpperInvariant();
        return string.CompareOrdinal(a, b) <= 0 ? (a, b) : (b, a);
    }

    /// <returns>Null when the two may travel together, otherwise the reason they may not.</returns>
    public string? Conflict(PlannableOrder x, PlannableOrder y)
    {
        if (separateHazardous && x.IsHazardous != y.IsHazardous)
        {
            return $"{(x.IsHazardous ? x.Number : y.Number)} is hazardous and {(x.IsHazardous ? y.Number : x.Number)} is not; dangerous goods travel apart.";
        }

        if (x.ProductCategory is { } a && y.ProductCategory is { } b && _blocked.Contains(Key(a, b)))
        {
            var why = _reasons.GetValueOrDefault(Key(a, b));
            return $"{x.Number} ({a}) cannot share a vehicle with {y.Number} ({b}){(string.IsNullOrWhiteSpace(why) ? "." : $": {why}")}";
        }

        return null;
    }

    public string? Conflict(IReadOnlyList<PlannableOrder> group)
    {
        for (var i = 0; i < group.Count; i++)
        {
            for (var j = i + 1; j < group.Count; j++)
            {
                if (Conflict(group[i], group[j]) is { } problem)
                {
                    return problem;
                }
            }
        }

        return null;
    }

    /// <summary>Splits orders into the fewest sets that can each share a vehicle (first-fit, in the given order).</summary>
    public List<List<PlannableOrder>> Partition(IEnumerable<PlannableOrder> orders)
    {
        var sets = new List<List<PlannableOrder>>();
        foreach (var order in orders)
        {
            var home = sets.FirstOrDefault(set => set.All(o => Conflict(o, order) is null));
            if (home is null)
            {
                sets.Add([order]);
            }
            else
            {
                home.Add(order);
            }
        }

        return sets;
    }
}
