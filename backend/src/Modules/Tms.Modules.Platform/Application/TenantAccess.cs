using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application;

/// <summary>What a person may do in their organisation: their roles' permissions, limited to the organisation's modules.</summary>
internal static class TenantAccess
{
    /// <summary>Held only by administrators of the operator's own organisation. It is not in the permission catalogue, so no role can be given it.</summary>
    public const string ManageTenants = "platform.tenants.manage";

    public static IReadOnlyList<string> PermissionsFor(User user, Tenant tenant)
    {
        var permissions = TmsModules.Filter(TmsModules.WithImplied(user.EffectivePermissions), tenant.Modules).ToList();
        if (tenant.IsPlatform && user.Roles.Any(r => r.IsSystem && r.Name == Role.AdministratorName))
        {
            permissions.Add(ManageTenants);
        }

        return permissions.Order(StringComparer.Ordinal).ToList();
    }
}
