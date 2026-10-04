using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Transporters.Domain;

public enum MasterKind
{
    /// <summary>What sort of operator a transporter is (full-truck, express, broker…).</summary>
    TransporterType = 1,

    /// <summary>What a transporter can be asked to carry (hazardous, temperature controlled…), beyond the built-in list.</summary>
    Capability = 2,
}

/// <summary>A tenant's own entry in a master list, or its override of a built-in one (renamed, or switched off). Removing never deletes: history keeps its meaning.</summary>
public sealed class MasterItem : AggregateRoot, ITenantScoped
{
    public const int MaxCodeLength = 40;

    private MasterItem()
    {
    }

    public Guid TenantId { get; private set; }

    public MasterKind Kind { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public bool IsActive { get; private set; } = true;

    public static string NormaliseCode(string? code) => (code ?? string.Empty).Trim().ToUpperInvariant().Replace(' ', '_').Replace('-', '_');

    public static Result<MasterItem> Create(Guid tenantId, MasterKind kind, string? code, string? name)
    {
        var item = new MasterItem { TenantId = tenantId, Kind = kind, Code = NormaliseCode(code) };
        var errors = new Dictionary<string, string[]>();
        if (item.Code.Length is 0 or > MaxCodeLength || !item.Code.All(c => c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_'))
        {
            errors["code"] = [$"Use letters, digits and underscores, up to {MaxCodeLength} characters."];
        }

        var renamed = item.Set(name, true);
        if (renamed.IsFailure && renamed.Error.ValidationErrors is { } nameErrors)
        {
            foreach (var (key, value) in nameErrors)
            {
                errors[key] = value;
            }
        }

        return errors.Count == 0
            ? item
            : Error.Validation("master.invalid", "One or more fields are invalid.") with { ValidationErrors = errors };
    }

    public Result Set(string? name, bool isActive)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
        {
            return Error.Validation("master.invalid", "Give it a name of up to 100 characters.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["name"] = ["Enter a name of up to 100 characters."] },
            };
        }

        Name = name.Trim();
        IsActive = isActive;
        return Result.Success();
    }
}

public sealed record MasterEntry(string Code, string Name, bool IsActive, bool IsBuiltIn);

/// <summary>The built-in lists merged with a tenant's own entries.</summary>
public static class MasterCatalog
{
    public static IReadOnlyList<(string Code, string Name)> BuiltInTypes { get; } =
    [
        ("FTL", "Full truck load"),
        ("PTL", "Part truck load"),
        ("EXPRESS", "Express"),
        ("DEDICATED", "Dedicated"),
        ("LAST_MILE", "Last mile"),
        ("MILK_RUN", "Milk run"),
        ("FLEET_OWNER", "Fleet owner"),
        ("BROKER", "Broker"),
        ("3PL", "Third-party logistics"),
    ];

    public static IReadOnlyList<MasterEntry> Merge(MasterKind kind, IEnumerable<MasterItem> tenantItems)
    {
        var builtIn = kind == MasterKind.TransporterType ? BuiltInTypes : CapabilityCatalog.Items;
        var own = tenantItems.Where(i => i.Kind == kind).ToDictionary(i => i.Code);
        var entries = builtIn.Select(b => own.TryGetValue(b.Code, out var o) ? new MasterEntry(b.Code, o.Name, o.IsActive, true) : new MasterEntry(b.Code, b.Name, true, true)).ToList();
        entries.AddRange(own.Values.Where(o => builtIn.All(b => b.Code != o.Code)).OrderBy(o => o.Code, StringComparer.Ordinal).Select(o => new MasterEntry(o.Code, o.Name, o.IsActive, false)));
        return entries;
    }

    public static bool IsActive(MasterKind kind, IEnumerable<MasterItem> tenantItems, string? code) =>
        Merge(kind, tenantItems).Any(e => e.IsActive && string.Equals(e.Code, MasterItem.NormaliseCode(code), StringComparison.Ordinal));
}
