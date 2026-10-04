using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Domain;

public static class ContractPermissions
{
    public const string Read = "contracts.read";
    public const string Manage = "contracts.manage";

    /// <summary>Decides contract approvals (referenced by the approval policy for <c>freight_contract</c>).</summary>
    public const string Approve = "contracts.approve";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "Contracts", "View freight contracts, rates and quotes"),
        new(Manage, "Contracts", "Create and edit contracts, rate cards, zones and diesel prices"),
        new(Approve, "Contracts", "Approve freight contracts"),
    ];
}
