using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application;

/// <summary>Prevents privilege escalation: nobody can hand out more access than they hold themselves.</summary>
internal static class RoleAssignmentPolicy
{
    public static bool CanGrant(ICurrentUser caller, IEnumerable<string> permissions) =>
        permissions.All(caller.Permissions.Contains);

    public static bool CanAssign(ICurrentUser caller, Role role) => CanGrant(caller, role.Permissions);
}
