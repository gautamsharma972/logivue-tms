using Microsoft.EntityFrameworkCore;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Telemetry;

namespace Tms.Modules.Platform.Application.Auth;

internal sealed class LoginHandler(PlatformDbContext db, IPasswordService passwords, SessionService sessions, TimeProvider clock)
{
    // Verified against when the account does not exist, so response time does not reveal which accounts are real.
    private static readonly Lazy<string> DummyHash = new(() => new PasswordService().Hash(Guid.NewGuid().ToString("N")));

    public async Task<Result<AuthResult>> HandleAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var tenantCode = request.TenantCode.Trim().ToUpperInvariant();
        var email = User.NormaliseEmail(request.Email);

        var tenant = await db.Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Code == tenantCode && t.IsActive, cancellationToken);

        // Login happens before a tenant is known to the caller, so the tenant filter is bypassed here and replaced
        // by an explicit TenantId predicate.
        var user = tenant is null
            ? null
            : await db.Users.IgnoreQueryFilters()
                .Include(u => u.Roles)
                .FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email, cancellationToken);

        if (user is null || tenant is null)
        {
            passwords.Verify(DummyHash.Value, request.Password);
            TmsTelemetry.LoginFailures.Add(1);
            return AuthErrors.InvalidCredentials;
        }

        var verification = passwords.Verify(user.PasswordHash, request.Password);
        if (verification == PasswordVerification.Failed)
        {
            if (!user.IsLockedOut(now))
            {
                user.RecordFailedLogin(now);
                await db.SaveChangesAsync(cancellationToken);
            }

            TmsTelemetry.LoginFailures.Add(1);
            return AuthErrors.InvalidCredentials;
        }

        // Only reveal lock / disabled state to someone who knows the password.
        if (!user.IsActive)
        {
            return AuthErrors.AccountDisabled;
        }

        if (user.IsLockedOut(now))
        {
            return AuthErrors.LockedOut;
        }

        if (verification == PasswordVerification.SuccessRehashNeeded)
        {
            user.SetPasswordHash(passwords.Hash(request.Password));
        }

        user.RecordSuccessfulLogin(now);
        TmsTelemetry.LoginSuccesses.Add(1);
        return await sessions.IssueAsync(user, tenant, Guid.CreateVersion7(), cancellationToken);
    }
}
