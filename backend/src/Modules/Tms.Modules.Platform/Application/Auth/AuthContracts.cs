using FluentValidation;
using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Platform.Application.Auth;

public sealed record LoginRequest(string TenantCode, string Email, string Password);

public sealed record UserProfile(
    Guid Id,
    string Email,
    string FullName,
    UserType Type,
    Guid? TransporterId,
    string TenantCode,
    string TenantName,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

/// <summary>What the client receives. The refresh token travels only in an HttpOnly cookie, never in the body.</summary>
public sealed record AuthResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, UserProfile User);

/// <summary>Handler output: the response plus the refresh token the endpoint turns into a cookie.</summary>
internal sealed record AuthResult(AuthResponse Response, string RefreshToken);

public sealed record ForgotPasswordRequest(string TenantCode, string Email);

public sealed record ResetPasswordRequest(string Token, string NewPassword);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

internal static class AuthErrors
{
    // One message for "no such tenant / no such user / wrong password" so the API cannot be used to enumerate accounts.
    public static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "The tenant, email or password is incorrect.");

    public static readonly Error LockedOut =
        Error.Unauthorized("auth.locked_out", "Too many failed attempts. The account is temporarily locked.");

    public static readonly Error AccountDisabled =
        Error.Unauthorized("auth.account_disabled", "This account has been deactivated.");

    public static readonly Error InvalidResetToken =
        Error.Validation("auth.reset_token_invalid", "This link is invalid or has expired. Request a new one.");

    public static readonly Error InvalidRefreshToken =
        Error.Unauthorized("auth.invalid_refresh_token", "The session is no longer valid. Please sign in again.");

    public static readonly Error RefreshTokenReuse =
        Error.Unauthorized("auth.refresh_token_reuse", "This session was already used and has been revoked. Please sign in again.");
}

internal sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.TenantCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}

internal sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.TenantCode).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
    }
}

internal sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).MustBeStrongPassword();
    }
}

internal sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).MustBeStrongPassword();
    }
}
