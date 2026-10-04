using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Tms.BuildingBlocks.Web.Email;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Platform.Application.Auth;

/// <summary>Creates single-use "choose a password" links and mails them. Shared by forgot-password and new-tenant invitations.</summary>
internal sealed class PasswordResetService(
    PlatformDbContext db,
    IEmailSender email,
    IOptions<EmailOptions> emailOptions,
    TimeProvider clock,
    ILogger<PasswordResetService> logger)
{
    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Invalidates any earlier unused links for the user and issues a new one. Returns the full link.</summary>
    public async Task<string> IssueLinkAsync(User user, TimeSpan validity, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var earlier = await db.PasswordResetTokens.IgnoreQueryFilters()
            .Where(t => t.UserId == user.Id && t.UsedAt == null)
            .ToListAsync(cancellationToken);
        earlier.ForEach(t => t.MarkUsed(now));

        var secret = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        db.PasswordResetTokens.Add(PasswordResetToken.Issue(user.TenantId, user.Id, Hash(secret), now + validity));
        await db.SaveChangesAsync(cancellationToken);

        return $"{emailOptions.Value.AppBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(secret)}";
    }

    public async Task SendResetAsync(User user, string link, CancellationToken cancellationToken)
    {
        await SendAsync(user.Email, "Reset your TMS password",
            $"Hello {user.FullName},\n\nSomeone asked to reset the password for your TMS account. If that was you, choose a new password here (the link works once and expires in 1 hour):\n\n{link}\n\nIf you did not ask for this, you can ignore this email; your password has not changed.",
            cancellationToken);
    }

    public async Task SendInviteAsync(User user, string tenantName, string link, int validDays, CancellationToken cancellationToken)
    {
        await SendAsync(user.Email, $"Your TMS account for {tenantName}",
            $"Hello {user.FullName},\n\nAn account has been created for you in TMS ({tenantName}). Choose your password here (the link works once and expires in {validDays} days):\n\n{link}",
            cancellationToken);
    }

    private async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(new EmailMessage(to, subject, body), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not send '{Subject}'", subject);
        }
    }
}

/// <summary>Always answers the same way, so the endpoint cannot be used to discover which emails have accounts.</summary>
internal sealed class ForgotPasswordHandler(PlatformDbContext db, PasswordResetService resets)
{
    public static readonly TimeSpan Validity = TimeSpan.FromHours(1);

    public async Task<Result> HandleAsync(ForgotPasswordRequest request, CancellationToken cancellationToken)
    {
        var code = request.TenantCode.Trim().ToUpperInvariant();
        var email = User.NormaliseEmail(request.Email);

        var tenant = await db.Tenants.AsNoTracking().FirstOrDefaultAsync(t => t.Code == code && t.IsActive, cancellationToken);
        var user = tenant is null
            ? null
            : await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId == tenant.Id && u.Email == email && u.IsActive, cancellationToken);

        if (user is not null)
        {
            var link = await resets.IssueLinkAsync(user, Validity, cancellationToken);
            await resets.SendResetAsync(user, link, cancellationToken);
        }

        return Result.Success();
    }
}

internal sealed class ResetPasswordHandler(PlatformDbContext db, IPasswordService passwords, SessionService sessions, TimeProvider clock)
{
    public async Task<Result> HandleAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var hash = PasswordResetService.Hash(request.Token);

        var token = await db.PasswordResetTokens.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);
        if (token is null || !token.IsUsableAt(now))
        {
            return AuthErrors.InvalidResetToken;
        }

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.Id == token.UserId && u.IsActive, cancellationToken);
        if (user is null)
        {
            return AuthErrors.InvalidResetToken;
        }

        user.ChangePassword(passwords.Hash(request.NewPassword));
        token.MarkUsed(now);
        await db.SaveChangesAsync(cancellationToken);

        // Whoever might hold an old session (that is often why people reset) is signed out.
        await sessions.RevokeAllForUserAsync(user.Id, cancellationToken);
        return Result.Success();
    }
}

internal sealed class ChangePasswordHandler(
    PlatformDbContext db,
    ICurrentUser currentUser,
    IPasswordService passwords,
    SessionService sessions)
{
    public async Task<Result<AuthResult>> HandleAsync(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users.Include(u => u.Roles).FirstOrDefaultAsync(u => u.Id == currentUser.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return AuthErrors.InvalidCredentials;
        }

        if (passwords.Verify(user.PasswordHash, request.CurrentPassword) == PasswordVerification.Failed)
        {
            return Error.Validation("auth.current_password_incorrect", "The current password is incorrect.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["currentPassword"] = ["The current password is incorrect."] },
            };
        }

        if (request.NewPassword == request.CurrentPassword)
        {
            return Error.Validation("auth.password_unchanged", "Choose a password different from the current one.") with
            {
                ValidationErrors = new Dictionary<string, string[]> { ["newPassword"] = ["Choose a password different from the current one."] },
            };
        }

        user.ChangePassword(passwords.Hash(request.NewPassword));
        await db.SaveChangesAsync(cancellationToken);

        // Every other session ends; this one continues with fresh tokens (the old access token still carries the forced-change flag).
        await sessions.RevokeAllForUserAsync(user.Id, cancellationToken);
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == user.TenantId, cancellationToken);
        return await sessions.IssueAsync(user, tenant, Guid.CreateVersion7(), cancellationToken);
    }
}
