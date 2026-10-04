using Tms.SharedKernel.Security;

namespace Tms.Modules.Approvals.Domain;

public static class ApprovalPermissions
{
    public const string PoliciesManage = "approvals.policies.manage";
    public const string ReadAll = "approvals.read.all";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(PoliciesManage, "Approvals", "Configure approval policies and thresholds"),
        new(ReadAll, "Approvals", "View every approval request in the organisation"),
    ];
}
