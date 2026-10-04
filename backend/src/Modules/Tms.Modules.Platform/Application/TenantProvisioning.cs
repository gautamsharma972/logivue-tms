using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Application.Auth;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Platform.Application;

public sealed record ProvisionTenantRequest(string Code, string Name, string AdminEmail, string AdminName);

/// <param name="InviteLink">One-time link for the administrator to choose their password. Hand it over securely; it expires.</param>
public sealed record ProvisionedTenant(Guid TenantId, string Code, string AdminEmail, string InviteLink, DateTimeOffset InviteExpiresAt);

/// <summary>
/// Operator action that creates a customer organisation with its first administrator. There is deliberately no public
/// HTTP endpoint for this: it is run by the platform operator (<c>tenant:create</c>). No password is ever generated or
/// transmitted; the administrator receives a single-use link to choose their own.
/// </summary>
internal sealed partial class TenantProvisioning(
    PlatformDbContext db,
    PermissionCatalog catalog,
    IPasswordService passwords,
    PasswordResetService resets)
{
    public const int InviteValidDays = 3;

    [GeneratedRegex("^[A-Z0-9][A-Z0-9_-]{1,31}$")]
    private static partial Regex CodePattern();

    public async Task<Result<ProvisionedTenant>> ProvisionAsync(ProvisionTenantRequest request, CancellationToken cancellationToken)
    {
        var code = request.Code.Trim().ToUpperInvariant();
        if (!CodePattern().IsMatch(code))
        {
            return Error.Validation("tenants.code_invalid", "The organisation code must be 2–32 characters: letters, digits, '-' or '_'.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || string.IsNullOrWhiteSpace(request.AdminName) || !request.AdminEmail.Contains('@', StringComparison.Ordinal))
        {
            return Error.Validation("tenants.details_invalid", "Organisation name, administrator name and a valid administrator email are required.");
        }

        if (await db.Tenants.AnyAsync(t => t.Code == code, cancellationToken))
        {
            return Error.Conflict("tenants.code_exists", $"An organisation with code {code} already exists.");
        }

        var tenant = Tenant.Create(code, request.Name);
        var admin = Role.Create(tenant.Id, Role.AdministratorName, "Full access to everything in this organisation.", catalog.Codes, isSystem: true);
        // The password hash is of random bytes nobody knows; the account is usable only via the invitation link.
        var unusable = passwords.Hash(Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48)));
        var user = User.Create(tenant.Id, request.AdminEmail, request.AdminName, unusable, UserType.Internal, [admin]);

        db.AddRange(tenant, admin, user);
        await db.SaveChangesAsync(cancellationToken);

        var validity = TimeSpan.FromDays(InviteValidDays);
        var link = await resets.IssueLinkAsync(user, validity, cancellationToken);
        await resets.SendInviteAsync(user, tenant.Name, link, InviteValidDays, cancellationToken);
        return new ProvisionedTenant(tenant.Id, tenant.Code, user.Email, link, DateTimeOffset.UtcNow + validity);
    }
}
