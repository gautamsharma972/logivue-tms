using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Domain;

public static class ShipmentPermissions
{
    public const string Read = "shipments.read";

    /// <summary>Create orders and shipments, choose and tender to transporters, dispatch and close.</summary>
    public const string Plan = "shipments.plan";

    /// <summary>Vendor portal: see loads tendered to your company, accept them with a vehicle and driver, or decline.</summary>
    /// <summary>Approve and commit a planning run. Kept separate so a supervisor can review what a planner drafted.</summary>
    public const string Approve = "shipments.approve";

    /// <summary>Accept or reject proofs of delivery. Staff only: a vendor cannot approve its own paperwork.</summary>
    public const string PodVerify = "shipments.pod.verify";

    public const string Respond = "shipments.respond";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "Shipments", "View orders, shipments and utilisation"),
        new(Plan, "Shipments", "Create orders and shipments, allocate transporters, dispatch and close"),
        new(Approve, "Shipments", "Approve and commit planning runs"),
        new(PodVerify, "Shipments", "Verify or reject proof of delivery"),
        new(Respond, "Shipments", "Vendor portal: accept or decline loads tendered to your company", ExternalAllowed: true),
    ];
}
