using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Validates real bearer tokens through the host's JWT configuration.</summary>
public class JwtAuthenticationTests : IClassFixture<JwtTestHost>
{
    private const string SigningKey = "test-signing-key-for-jwt-checks-0123456789abcdef";
    private readonly JwtTestHost host;

    public JwtAuthenticationTests(JwtTestHost host)
    {
        this.host = host;
        host.EnsureTransporterSchemaAsync().GetAwaiter().GetResult();
    }

    private static string Token(IEnumerable<Claim> claims) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("logivue-tms", "logivue-tms-api", claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey)), SecurityAlgorithms.HmacSha256)));

    [Fact]
    public async Task A_signed_token_with_roles_authenticates_and_is_authorised()
    {
        var token = Token([new Claim("sub", "ops-1"), new Claim("roles", "Transport Manager")]);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/transporter-management/lookups/service-types");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_token_signed_with_another_key_is_refused()
    {
        var forged = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("logivue-tms", "logivue-tms-api",
            [new Claim("sub", "intruder"), new Claim("roles", "Transport Admin")], expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-different-signing-key-0123456789abcdef")), SecurityAlgorithms.HmacSha256)));
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        var response = await client.GetAsync("/api/v1/transporter-management/lookups/service-types");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
