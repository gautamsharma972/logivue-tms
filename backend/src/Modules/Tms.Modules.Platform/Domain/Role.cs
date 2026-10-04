using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Platform.Domain;

/// <summary>Who a role is for. Keeps vendor-portal accounts structurally unable to hold staff permissions.</summary>
public enum RoleAudience
{
    /// <summary>Staff of the shipper organisation.</summary>
    Internal = 0,

    /// <summary>Vendor-portal users and drivers. May only contain permissions marked external-allowed.</summary>
    External = 1,
}

public sealed class Role : AggregateRoot, ITenantScoped
{
    public const string AdministratorName = "Administrator";

    private Role()
    {
    }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    /// <summary>System roles are managed by the platform (e.g. Administrator always holds every permission).</summary>
    public bool IsSystem { get; private set; }

    public RoleAudience Audience { get; private set; } = RoleAudience.Internal;

    public IReadOnlyList<string> Permissions { get; private set; } = [];

    public static Role Create(Guid tenantId, string name, string? description, IEnumerable<string> permissions, bool isSystem = false, RoleAudience audience = RoleAudience.Internal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Role
        {
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description?.Trim(),
            IsSystem = isSystem,
            Audience = audience,
            Permissions = Normalise(permissions),
        };
    }

    public Result Update(string name, string? description, IEnumerable<string> permissions)
    {
        if (IsSystem)
        {
            return Error.Conflict("roles.system_immutable", "System roles cannot be modified.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Description = description?.Trim();
        Permissions = Normalise(permissions);
        return Result.Success();
    }

    /// <summary>Keeps system roles in step with the permission catalog as modules add new permissions.</summary>
    internal void SyncSystemPermissions(IEnumerable<string> allPermissions)
    {
        if (!IsSystem)
        {
            return;
        }

        var synced = Normalise(allPermissions);
        if (!synced.SequenceEqual(Permissions))
        {
            Permissions = synced;
        }
    }

    private static string[] Normalise(IEnumerable<string> permissions) =>
        permissions.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
}
