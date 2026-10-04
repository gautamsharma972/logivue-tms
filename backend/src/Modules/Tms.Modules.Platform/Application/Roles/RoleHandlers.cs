using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application.Roles;

internal sealed class ListRolesHandler(PlatformDbContext db)
{
    public async Task<Result<IReadOnlyList<RoleDto>>> HandleAsync(CancellationToken cancellationToken)
    {
        var rows = await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new { Role = r, UserCount = db.Users.Count(u => u.Roles.Any(x => x.Id == r.Id)) })
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<RoleDto>>(rows.Select(r => r.Role.ToDto(r.UserCount)).ToList());
    }
}

internal sealed class CreateRoleHandler(PlatformDbContext db, ICurrentUser currentUser, PermissionCatalog catalog)
{
    public async Task<Result<RoleDto>> HandleAsync(SaveRoleRequest request, CancellationToken cancellationToken)
    {
        if (request.Permissions.FirstOrDefault(p => !catalog.Contains(p)) is { } unknown)
        {
            return Error.Validation("roles.unknown_permission", $"Unknown permission '{unknown}'.");
        }

        if (!RoleAssignmentPolicy.CanGrant(currentUser, request.Permissions))
        {
            return Error.Forbidden("roles.permission_escalation", "You cannot grant permissions that you do not hold.");
        }

        var audience = request.Audience ?? RoleAudience.Internal;
        if (RoleAudienceRules.Check(catalog, audience, request.Permissions) is { } notExternal)
        {
            return notExternal;
        }

        var name = request.Name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == name, cancellationToken))
        {
            return Error.Conflict("roles.name_taken", $"A role named '{name}' already exists.");
        }

        var role = Role.Create(currentUser.TenantId!.Value, name, request.Description, request.Permissions, audience: audience);
        db.Roles.Add(role);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Error.Conflict("roles.name_taken", $"A role named '{name}' already exists.");
        }

        return role.ToDto(0);
    }
}

internal static class RoleAudienceRules
{
    public static Error? Check(PermissionCatalog catalog, RoleAudience audience, IEnumerable<string> permissions)
    {
        if (audience != RoleAudience.External)
        {
            return null;
        }

        var internalOnly = permissions.FirstOrDefault(p => !catalog.IsExternalAllowed(p));
        return internalOnly is null
            ? null
            : Error.Validation("roles.permission_not_external", $"'{internalOnly}' is a staff-only permission and cannot be given to an external (vendor or driver) role.");
    }
}

internal sealed class UpdateRoleHandler(PlatformDbContext db, ICurrentUser currentUser, PermissionCatalog catalog)
{
    public async Task<Result<RoleDto>> HandleAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken)
    {
        if (request.Version is null)
        {
            return Error.Validation("roles.version_required", "The current version of the role is required.");
        }

        if (request.Permissions.FirstOrDefault(p => !catalog.Contains(p)) is { } unknown)
        {
            return Error.Validation("roles.unknown_permission", $"Unknown permission '{unknown}'.");
        }

        var role = await db.Roles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (role is null)
        {
            return Error.NotFound("roles.not_found", "Role not found.");
        }

        // Changing a role is granting its permissions to everyone who holds it.
        if (!RoleAssignmentPolicy.CanGrant(currentUser, request.Permissions))
        {
            return Error.Forbidden("roles.permission_escalation", "You cannot grant permissions that you do not hold.");
        }

        var name = request.Name.Trim();
        if (await db.Roles.AnyAsync(r => r.Name == name && r.Id != id, cancellationToken))
        {
            return Error.Conflict("roles.name_taken", $"A role named '{name}' already exists.");
        }

        if (RoleAudienceRules.Check(catalog, role.Audience, request.Permissions) is { } notExternal)
        {
            return notExternal;
        }

        db.Entry(role).Property(r => r.Version).OriginalValue = request.Version.Value;
        var updated = role.Update(name, request.Description, request.Permissions);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await db.SaveChangesAsync(cancellationToken);

        var userCount = await db.Users.CountAsync(u => u.Roles.Any(x => x.Id == id), cancellationToken);
        return role.ToDto(userCount);
    }
}

internal sealed class ListPermissionsHandler(PermissionCatalog catalog)
{
    public Result<IReadOnlyList<PermissionDefinition>> Handle() => Result.Success(catalog.All);
}
