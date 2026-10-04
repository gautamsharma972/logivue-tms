using Microsoft.AspNetCore.Identity;

namespace Tms.Modules.Platform.Infrastructure.Security;

internal enum PasswordVerification
{
    Failed,
    Success,
    SuccessRehashNeeded,
}

internal interface IPasswordService
{
    string Hash(string password);

    PasswordVerification Verify(string hash, string password);
}

/// <summary>PBKDF2 (ASP.NET Core Identity v3 format) without taking a dependency on the full Identity stack.</summary>
internal sealed class PasswordService : IPasswordService
{
    private static readonly object Subject = new();
    private readonly PasswordHasher<object> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(Subject, password);

    public PasswordVerification Verify(string hash, string password) =>
        _hasher.VerifyHashedPassword(Subject, hash, password) switch
        {
            PasswordVerificationResult.Success => PasswordVerification.Success,
            PasswordVerificationResult.SuccessRehashNeeded => PasswordVerification.SuccessRehashNeeded,
            _ => PasswordVerification.Failed,
        };
}
