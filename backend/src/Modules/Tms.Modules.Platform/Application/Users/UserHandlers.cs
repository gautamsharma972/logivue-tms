using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application.Users;

internal static class RoleResolver
{
    /// <summary>Loads the requested roles (tenant-filtered) and checks the caller may hand them out.</summary>
    public static async Task<Result<List<Role>>> ResolveAsync(
        PlatformDbContext db,
        ICurrentUser caller,
        IReadOnlyList<Guid> roleIds,
        UserType userType,
        CancellationToken cancellationToken)
    {
        var ids = roleIds.Distinct().ToList();
        var roles = await db.Roles.Where(r => ids.Contains(r.Id)).ToListAsync(cancellationToken);

        if (roles.Count != ids.Count)
        {
            return Error.Validation("users.invalid_roles", "One or more roles do not exist.");
        }

        var expected = userType == UserType.Internal ? RoleAudience.Internal : RoleAudience.External;
        if (roles.FirstOrDefault(r => r.Audience != expected) is { } mismatch)
        {
            return Error.Validation("users.role_audience", userType == UserType.Internal
                ? $"'{mismatch.Name}' is an external (vendor/driver) role and cannot be given to staff."
                : $"'{mismatch.Name}' is a staff role and cannot be given to a vendor or driver account.");
        }

        if (roles.Any(r => !RoleAssignmentPolicy.CanAssign(caller, r)))
        {
            return Error.Forbidden("users.role_escalation", "You cannot assign roles that carry permissions you do not hold.");
        }

        return roles;
    }
}

internal sealed class ListUsersHandler(PlatformDbContext db)
{
    public async Task<Result<PagedResult<UserDto>>> HandleAsync(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var users = db.Users.AsNoTracking().Include(u => u.Roles).AsQueryable();

        if (query.IsActive is { } active)
        {
            users = users.Where(u => u.IsActive == active);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            users = users.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));
        }

        var page = await users.OrderBy(u => u.FullName).ThenBy(u => u.Id).ToPagedAsync(query.Page, query.PageSize, cancellationToken);
        return new PagedResult<UserDto>(page.Items.Select(u => u.ToDto()).ToList(), page.Page, page.PageSize, page.TotalCount);
    }
}

internal sealed class GetUserHandler(PlatformDbContext db)
{
    public async Task<Result<UserDto>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        return user is null ? Error.NotFound("users.not_found", "User not found.") : user.ToDto();
    }
}

internal sealed class CreateUserHandler(PlatformDbContext db, ICurrentUser currentUser, IPasswordService passwords, ITransporterDirectory transporters)
{
    public async Task<Result<UserDto>> HandleAsync(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = User.NormaliseEmail(request.Email);
        var conflict = Error.Conflict("users.email_taken", $"A user with email '{email}' already exists.");

        if (await db.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            return conflict;
        }

        if (request.Type != UserType.Internal != (request.TransporterId is not null))
        {
            return Error.Validation("users.transporter_link", "Transporter and driver users must be linked to a transporter; staff users must not be.");
        }

        if (request.TransporterId is { } transporterId && !await transporters.ExistsAsync(transporterId, cancellationToken))
        {
            return Error.Validation("users.transporter_unknown", "The selected transporter does not exist.");
        }

        var roles = await RoleResolver.ResolveAsync(db, currentUser, request.RoleIds, request.Type, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        var user = User.Create(currentUser.TenantId!.Value, email, request.FullName, passwords.Hash(request.Password), request.Type, roles.Value, request.TransporterId, request.RequirePasswordChange);
        db.Users.Add(user);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return conflict;
        }

        return user.ToDto();
    }
}

internal sealed class UpdateUserHandler(PlatformDbContext db, ICurrentUser currentUser)
{
    public async Task<Result<UserDto>> HandleAsync(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("users.not_found", "User not found.");
        }

        if (!request.IsActive && user.Id == currentUser.UserId)
        {
            return Error.Conflict("users.cannot_deactivate_self", "You cannot deactivate your own account.");
        }

        var roles = await RoleResolver.ResolveAsync(db, currentUser, request.RoleIds, user.Type, cancellationToken);
        if (roles.IsFailure)
        {
            return roles.Error;
        }

        // Removing a role the caller could not have granted is also an escalation-style change (e.g. demoting an admin).
        var removed = user.Roles.Where(r => roles.Value.All(n => n.Id != r.Id));
        if (removed.Any(r => !RoleAssignmentPolicy.CanAssign(currentUser, r)))
        {
            return Error.Forbidden("users.role_escalation", "You cannot remove roles that carry permissions you do not hold.");
        }

        db.Entry(user).Property(u => u.Version).OriginalValue = request.Version;
        user.UpdateProfile(request.FullName);
        user.SetActive(request.IsActive);
        user.SetRoles(roles.Value);

        await db.SaveChangesAsync(cancellationToken);
        return user.ToDto();
    }
}
