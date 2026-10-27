using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Application.Auth;
using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application.Tenants;

public sealed record TenantDto(Guid Id, string Code, string Name, bool IsActive, bool IsPlatform, IReadOnlyList<string> Modules, int Users, string? FirstAdminEmail = null, int? MaxUsers = null, int? MaxLocations = null, int ActiveUsers = 0);

/// <param name="ResetLink">Single-use link for choosing a new password. Shown to the platform administrator once; it is also emailed to the administrator.</param>
public sealed record AdminPasswordResetDto(string AdminEmail, string AdminName, string ResetLink, DateTimeOffset ExpiresAt);

public sealed record ModuleDto(string Key, string Name, string Description);

public sealed record SetTenantModulesRequest(IReadOnlyList<string> Modules);

public sealed record CreateTenantRequest(string Code, string Name, string AdminName, string AdminEmail, IReadOnlyList<string> Modules, int? MaxUsers = null, int? MaxLocations = null);

/// <summary>Edit an organisation: its name, its modules and its limits (null = unlimited). The code never changes.</summary>
public sealed record UpdateTenantRequest(string Name, IReadOnlyList<string> Modules, int? MaxUsers, int? MaxLocations);

internal sealed class UpdateTenantRequestValidator : AbstractValidator<UpdateTenantRequest>
{
    public UpdateTenantRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Modules).NotNull();
        RuleForEach(x => x.Modules).Must(TmsModules.Keys.Contains).WithMessage("Unknown module '{PropertyValue}'.");
        RuleFor(x => x.MaxUsers).GreaterThanOrEqualTo(1).When(x => x.MaxUsers is not null);
        RuleFor(x => x.MaxLocations).GreaterThanOrEqualTo(0).When(x => x.MaxLocations is not null);
    }
}

internal sealed class CreateTenantRequestValidator : AbstractValidator<CreateTenantRequest>
{
    public CreateTenantRequestValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdminName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.AdminEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Modules).NotNull();
        RuleForEach(x => x.Modules).Must(TmsModules.Keys.Contains).WithMessage("Unknown module '{PropertyValue}'.");
        RuleFor(x => x.MaxUsers).GreaterThanOrEqualTo(1).When(x => x.MaxUsers is not null);
        RuleFor(x => x.MaxLocations).GreaterThanOrEqualTo(0).When(x => x.MaxLocations is not null);
    }
}

internal sealed class SetTenantModulesRequestValidator : AbstractValidator<SetTenantModulesRequest>
{
    public SetTenantModulesRequestValidator()
    {
        RuleFor(x => x.Modules).NotNull();
        RuleForEach(x => x.Modules).Must(TmsModules.Keys.Contains).WithMessage("Unknown module '{PropertyValue}'.");
    }
}

/// <summary>
/// The operator's view of customer organisations and which modules each one has. Only administrators of the platform organisation reach it; anyone else gets the same answer as for a missing page.
/// </summary>
internal sealed class TenantAdminHandler(PlatformDbContext db, ICurrentUser currentUser, IServiceScopeFactory scopes)
{
    public static IReadOnlyList<ModuleDto> Modules() => TmsModules.All.Select(m => new ModuleDto(m.Key, m.Name, m.Description)).ToList();

    public async Task<Result<IReadOnlyList<TenantDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!await IsPlatformCallerAsync(cancellationToken))
        {
            return NotFound;
        }

        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(cancellationToken);
        var counts = await db.Users.IgnoreQueryFilters().GroupBy(u => u.TenantId).Select(g => new { g.Key, Count = g.Count(), Active = g.Count(u => u.IsActive) }).ToListAsync(cancellationToken);
        var admins = await db.Users.IgnoreQueryFilters().Where(u => u.Roles.Any(r => r.IsSystem && r.Name == Role.AdministratorName))
            .Select(u => new { u.TenantId, u.Email, u.CreatedAt }).ToListAsync(cancellationToken);
        var firstAdmin = admins.GroupBy(a => a.TenantId).ToDictionary(g => g.Key, g => g.OrderBy(a => a.CreatedAt).First().Email);
        return Result.Success<IReadOnlyList<TenantDto>>(tenants.Select(t => ToDto(t, counts.FirstOrDefault(c => c.Key == t.Id)?.Count ?? 0) with { FirstAdminEmail = firstAdmin.GetValueOrDefault(t.Id), ActiveUsers = counts.FirstOrDefault(c => c.Key == t.Id)?.Active ?? 0 }).ToList());
    }

    /// <summary>Creates a customer organisation and its first administrator. The administrator chooses their own password through the single-use link (also emailed); the link is shown to the operator once.</summary>
    public async Task<Result<ProvisionedTenant>> CreateAsync(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        if (!await IsPlatformCallerAsync(cancellationToken))
        {
            return NotFound;
        }

        // The new organisation's rows belong to another tenant, which a request scoped to the platform organisation may not write. Provisioning therefore runs in its own scope with no tenant,
        // exactly as the tenant:create command does, but still attributed to the platform administrator in the audit trail.
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IAmbientUserContext>().RunAs(null, currentUser.UserId, currentUser.TraceId);
        return await scope.ServiceProvider.GetRequiredService<TenantProvisioning>()
            .ProvisionAsync(new ProvisionTenantRequest(request.Code, request.Name, request.AdminEmail, request.AdminName, request.Modules, MaxUsers: request.MaxUsers, MaxLocations: request.MaxLocations), cancellationToken);
    }

    public const int ResetValidMinutes = PasswordResetService.ResetMinutes;

    /// <summary>
    /// Issues a single-use password link, valid for 10 minutes, for an organisation's first administrator (the earliest Administrator account), for when they are locked out or the invitation was lost.
    /// Their current password keeps working until they use the link. The link is returned once and mailed to them; no password is ever chosen or seen here.
    /// </summary>
    public async Task<Result<AdminPasswordResetDto>> ResetFirstAdminPasswordAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        if (!await IsPlatformCallerAsync(cancellationToken))
        {
            return NotFound;
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IAmbientUserContext>().RunAs(null, currentUser.UserId, currentUser.TraceId);
        var scopedDb = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var tenant = await scopedDb.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            return NotFound;
        }

        var admin = (await scopedDb.Users.IgnoreQueryFilters().Include(u => u.Roles)
                .Where(u => u.TenantId == tenantId && u.Roles.Any(r => r.IsSystem && r.Name == Role.AdministratorName))
                .ToListAsync(cancellationToken))
            .OrderBy(u => u.CreatedAt).FirstOrDefault();
        if (admin is null)
        {
            return Error.Conflict("tenants.no_admin", "This organisation has no administrator account.");
        }

        if (!admin.IsActive)
        {
            return Error.Conflict("tenants.admin_inactive", "The first administrator's account is deactivated. Another administrator of the organisation must reactivate it.");
        }

        var resets = scope.ServiceProvider.GetRequiredService<PasswordResetService>();
        var validity = TimeSpan.FromMinutes(ResetValidMinutes);
        var link = await resets.IssueLinkAsync(admin, validity, cancellationToken);
        await resets.SendAdminResetAsync(admin, tenant.Name, link, ResetValidMinutes, cancellationToken);
        return new AdminPasswordResetDto(admin.Email, admin.FullName, link, DateTimeOffset.UtcNow + validity);
    }

    public async Task<Result<TenantDto>> SetModulesAsync(Guid id, SetTenantModulesRequest request, CancellationToken cancellationToken)
    {
        if (!await IsPlatformCallerAsync(cancellationToken))
        {
            return NotFound;
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return NotFound;
        }

        if (tenant.IsPlatform)
        {
            return Error.Conflict("tenants.platform_fixed", "The platform organisation is not a customer; its modules cannot be changed.");
        }

        tenant.SetModules(request.Modules);
        await db.SaveChangesAsync(cancellationToken);
        var users = await db.Users.IgnoreQueryFilters().CountAsync(u => u.TenantId == tenant.Id, cancellationToken);
        return ToDto(tenant, users);
    }

    public async Task<Result<TenantDto>> UpdateAsync(Guid id, UpdateTenantRequest request, CancellationToken cancellationToken)
    {
        if (!await IsPlatformCallerAsync(cancellationToken))
        {
            return NotFound;
        }

        var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (tenant is null)
        {
            return NotFound;
        }

        if (tenant.IsPlatform)
        {
            return Error.Conflict("tenants.platform_fixed", "The platform organisation is not a customer; it cannot be edited here.");
        }

        tenant.Rename(request.Name);
        tenant.SetModules(request.Modules);
        tenant.SetLimits(request.MaxUsers, request.MaxLocations);
        await db.SaveChangesAsync(cancellationToken);
        var users = await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == tenant.Id).Select(u => u.IsActive).ToListAsync(cancellationToken);
        return ToDto(tenant, users.Count) with { ActiveUsers = users.Count(a => a) };
    }

    private static readonly Error NotFound = Error.NotFound("tenants.not_found", "Not found.");

    private async Task<bool> IsPlatformCallerAsync(CancellationToken cancellationToken) =>
        currentUser.TenantId is { } tenantId && await db.Tenants.AsNoTracking().AnyAsync(t => t.Id == tenantId && t.IsPlatform, cancellationToken);

    private static TenantDto ToDto(Domain.Tenant t, int users) =>
        new(t.Id, t.Code, t.Name, t.IsActive, t.IsPlatform, (t.Modules ?? TmsModules.Keys).Order(StringComparer.Ordinal).ToList(), users, null, t.MaxUsers, t.MaxLocations);
}
