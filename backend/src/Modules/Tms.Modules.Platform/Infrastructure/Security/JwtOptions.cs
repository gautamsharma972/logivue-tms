using System.ComponentModel.DataAnnotations;

namespace Tms.Modules.Platform.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; init; } = null!;

    [Required]
    public string Audience { get; init; } = null!;

    /// <summary>HMAC-SHA256 key. Must come from a secret store outside development.</summary>
    [Required]
    [MinLength(32, ErrorMessage = "Jwt:SigningKey must be at least 32 characters.")]
    public string SigningKey { get; init; } = null!;

    [Range(1, 120)]
    public int AccessTokenMinutes { get; init; } = 15;

    [Range(1, 90)]
    public int RefreshTokenDays { get; init; } = 14;
}
