using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Domain;

public static class ContractPermissions
{
    public const string Read = "contracts.read";
    public const string Manage = "contracts.manage";

    /// <summary>Decides contract approvals (referenced by the approval policy for <c>freight_contract</c>).</summary>
    public const string Approve = "contracts.approve";

    /// <summary>Keep a rating as a shipment's freight and see the full calculation trace.</summary>
    public const string Rate = "contracts.rate";

    /// <summary>Agree a freight that differs from the calculated one. Always with a reason.</summary>
    public const string Override = "contracts.rating.override";

    /// <summary>Verify contract documents and activate imported rates (a separate duty from preparing them).</summary>
    public const string Verify = "contracts.verify";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "Contracts", "View freight contracts, rates and quotes"),
        new(Manage, "Contracts", "Create and edit contracts, rate cards, zones and diesel prices"),
        new(Approve, "Contracts", "Approve freight contracts"),
        new(Rate, "Contracts", "Keep freight ratings against shipments and see their calculation trace"),
        new(Override, "Contracts", "Override a rated freight with an agreed amount (a reason is required)"),
        new(Verify, "Contracts", "Verify contract documents"),
    ];
}
