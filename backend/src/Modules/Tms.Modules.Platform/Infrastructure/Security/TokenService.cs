using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Tms.BuildingBlocks.Web.Security;
using Tms.Modules.Platform.Domain;

namespace Tms.Modules.Platform.Infrastructure.Security;

internal sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);

internal interface ITokenService
{
    AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions);

    /// <summary>Returns a fresh opaque refresh token and the hash that is persisted.</summary>
    (string Token, string Hash) CreateRefreshToken();

    string HashRefreshToken(string token);
}

internal sealed class TokenService(IOptions<JwtOptions> options, TimeProvider clock) : ITokenService
{
    private readonly JwtOptions _options = options.Value;
    private readonly SigningCredentials _credentials =
        new(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.Value.SigningKey)), SecurityAlgorithms.HmacSha256);

    public AccessToken CreateAccessToken(User user, IReadOnlyCollection<string> permissions)
    {
        var now = clock.GetUtcNow();
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _credentials,
            Claims = new Dictionary<string, object>
            {
                [TmsClaimTypes.Subject] = user.Id.ToString(),
                [TmsClaimTypes.Tenant] = user.TenantId.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [JwtRegisteredClaimNames.Name] = user.FullName,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                ["user_type"] = user.Type.ToString(),
                [TmsClaimTypes.Permission] = permissions.ToArray(),
            },
        };

        if (user.MustChangePassword)
        {
            descriptor.Claims[TmsClaimTypes.MustChangePassword] = "1";
        }

        if (user.TransporterId is { } transporterId)
        {
            descriptor.Claims[TmsClaimTypes.Transporter] = transporterId.ToString();
        }

        return new AccessToken(new JsonWebTokenHandler().CreateToken(descriptor), expires);
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        var token = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(48));
        return (token, HashRefreshToken(token));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
