namespace Tms.SharedKernel.Security;

public sealed record ModuleDefinition(string Key, string Name, string Description);

/// <summary>
/// The business modules a customer organisation can be given. A permission belongs to at most one module; everything else (users, roles, audit, approvals) is core and always available.
/// A tenant with no module list has every module. An organisation without a module simply never holds that module's permissions, so its API, menu and reports disappear together.
/// </summary>
public static class TmsModules
{
    public const string TransportOps = "transport-ops";
    public const string Transporters = "transporters";
    public const string Contracts = "contracts";
    public const string Shipments = "shipments";
    public const string Deliveries = "deliveries";
    public const string Tracking = "tracking";
    public const string Reports = "reports";

    public static IReadOnlyList<ModuleDefinition> All { get; } =
    [
        new(TransportOps, "Transporter Operations", "Delivery requests, LRs, trips, POD, freight and detention billing, invoices and customer terms"),
        new(Transporters, "Transporter management", "Transporter master, vehicle types, performance, rankings, selection, placements and compliance"),
        new(Contracts, "Freight contracts", "Contracts, rate management, rating, diesel adjustment, extra charges and renewals"),
        new(Shipments, "Orders, planning & shipments", "Locations, orders, load planning, shipments and tendering"),
        new(Deliveries, "Delivery & proof of delivery", "Delivery execution, proofs, review, exceptions and notifications"),
        new(Tracking, "Shipment tracking", "Control tower, live vehicle tracking, geofences and alerts"),
        new(Reports, "Reports & analytics", "The report catalogue, exports and schedules, for the modules the organisation has"),
    ];

    public static IReadOnlySet<string> Keys { get; } = All.Select(m => m.Key).ToHashSet(StringComparer.Ordinal);

    /// <summary>Report permissions follow the module whose data the reports read. Executive and cross-module reports belong to Reports itself.</summary>
    private static readonly Dictionary<string, string> Exact = new(StringComparer.Ordinal)
    {
        ["reports.operations"] = TransportOps,
        ["reports.transporters"] = Transporters,
        ["reports.contracts"] = Contracts,
        ["reports.planning"] = Shipments,
        ["reports.delivery"] = Deliveries,
        ["reports.tracking"] = Tracking,
    };

    private static readonly (string Prefix, string Module)[] Prefixes =
    [
        ("ops.", TransportOps), ("transporters.", Transporters), ("contracts.", Contracts), ("shipments.", Shipments), ("deliveries.", Deliveries), ("tracking.", Tracking), ("reports.", Reports),
    ];

    /// <summary>The module a permission belongs to, or null for core permissions.</summary>
    public static string? ModuleOf(string permission)
    {
        if (Exact.TryGetValue(permission, out var module))
        {
            return module;
        }

        return Prefixes.FirstOrDefault(p => permission.StartsWith(p.Prefix, StringComparison.Ordinal)).Module;
    }

    /// <summary>Transporter Operations actions that make no sense without seeing the records they act on.</summary>
    private static readonly string[] OpsActions =
    [
        "ops.dispatch", "ops.masters.manage", "ops.pod.upload", "ops.pod.review", "ops.arrival.override", "ops.configure",
    ];

    /// <summary>Money actions: they imply seeing the rates, billing and invoices they work on (and, as before, the operational records).</summary>
    private static readonly string[] OpsBillingActions = ["ops.commercial.manage", "ops.billing.calculate", "ops.invoice.manage", "ops.payment.manage"];

    /// <summary>Adds the permissions that are implied: whoever may act on Operations records may also read them, so a role cannot be left able to dispatch yet unable to see an LR. Seeing prices is separate: only the billing actions imply ops.billing.read.</summary>
    public static IEnumerable<string> WithImplied(IEnumerable<string> permissions)
    {
        var all = permissions.ToHashSet(StringComparer.Ordinal);
        if (all.Overlaps(OpsActions) || all.Overlaps(OpsBillingActions))
        {
            all.Add("ops.read");
        }

        if (all.Overlaps(OpsBillingActions))
        {
            all.Add("ops.billing.read");
        }

        return all;
    }

    /// <summary>Null <paramref name="enabled"/> means every module.</summary>
    public static bool Allows(IReadOnlySet<string>? enabled, string permission) =>
        enabled is null || ModuleOf(permission) is not { } module || enabled.Contains(module);

    public static IEnumerable<string> Filter(IEnumerable<string> permissions, IReadOnlySet<string>? enabled) => permissions.Where(p => Allows(enabled, p));

    /// <summary>Parses the stored comma list; null means every module, an empty string means none. Unknown keys are dropped.</summary>
    public static IReadOnlySet<string>? Parse(string? stored) =>
        stored is null
            ? null
            : stored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Keys.Contains).ToHashSet(StringComparer.Ordinal);
}
