using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Domain;

public static class Permissions
{
    public const string UsersRead = "users.read";
    public const string UsersManage = "users.manage";
    public const string RolesRead = "roles.read";
    public const string RolesManage = "roles.manage";
    public const string AuditRead = "audit.read";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(UsersRead, "Administration", "View users"),
        new(UsersManage, "Administration", "Create, edit and deactivate users"),
        new(RolesRead, "Administration", "View roles and permissions"),
        new(RolesManage, "Administration", "Create and edit roles"),
        new(AuditRead, "Administration", "View the audit trail"),
    ];
}
