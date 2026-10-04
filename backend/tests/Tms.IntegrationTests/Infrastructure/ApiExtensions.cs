using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tms.Modules.Platform.Application.Auth;
using Tms.Modules.Platform.Application.Roles;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;

namespace Tms.IntegrationTests.Infrastructure;

internal static class ApiExtensions
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Json);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, Json);

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(text, Json) ?? throw new InvalidOperationException($"Empty body: {text}");
    }

    /// <summary>Reads the machine-readable <c>code</c> from a problem-details response.</summary>
    public static async Task<string?> ProblemCodeAsync(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    /// <summary>The refresh-token cookie value from a response's Set-Cookie header (null if none).</summary>
    public static string? RefreshCookieFrom(this HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.Select(v => v.Split(';')[0]).FirstOrDefault(v => v.StartsWith("tms_refresh=", StringComparison.Ordinal))?["tms_refresh=".Length..]
            : null;

    /// <summary>Logs in with a client that does NOT manage cookies, returning the cookie so tests can replay it explicitly.</summary>
    public static async Task<(AuthResponse Auth, string Cookie)> LoginWithCookieAsync(this TmsApiFactory factory, string tenant, string email, string password)
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var response = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest(tenant, email, password));
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return (await response.ReadAsync<AuthResponse>(), response.RefreshCookieFrom().ShouldNotBeNull());
    }

    public static async Task<HttpResponseMessage> RefreshWithAsync(this TmsApiFactory factory, string? cookie, bool csrfHeader = true)
    {
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"tms_refresh={cookie}");
        }

        if (csrfHeader)
        {
            request.Headers.Add("X-TMS-Client", "tests");
        }

        return await client.SendAsync(request);
    }

    public static async Task<AuthResponse> LoginAsync(this HttpClient client, string tenant, string email, string password)
    {
        var response = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest(tenant, email, password));
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<AuthResponse>();
    }

    /// <summary>A client already signed in as the given user.</summary>
    public static async Task<HttpClient> SignedInAsync(this TmsApiFactory factory, string tenant, string email, string password)
    {
        var client = factory.CreateClient();
        var auth = await client.LoginAsync(tenant, email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    public static Task<HttpClient> AdminAsync(this TmsApiFactory factory) =>
        factory.SignedInAsync(TmsApiFactory.DemoTenant, TmsApiFactory.DemoAdminEmail, TmsApiFactory.DemoAdminPassword);

    public const string StrongPassword = "Str0ng#Passw0rd!";

    public static string UniqueEmail(string prefix = "user") => $"{prefix}.{Guid.NewGuid():N}@demo.tms";

    public static async Task<UserDto> CreateUserAsync(this HttpClient admin, string? email = null, IReadOnlyList<Guid>? roleIds = null)
    {
        var response = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            email ?? UniqueEmail(), "Test User", StrongPassword, UserType.Internal, roleIds ?? []));
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<UserDto>();
    }

    public static async Task<RoleDto> CreateRoleAsync(this HttpClient admin, params string[] permissions)
    {
        var response = await admin.PostJsonAsync("/api/v1/roles", new SaveRoleRequest($"Role {Guid.NewGuid():N}", null, permissions, null));
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RoleDto>();
    }

    public static async Task<RoleDto> CreateExternalRoleAsync(this HttpClient admin, params string[] permissions)
    {
        var response = await admin.PostJsonAsync("/api/v1/roles",
            new SaveRoleRequest($"External {Guid.NewGuid():N}", null, permissions, null, RoleAudience.External));
        response.StatusCode.ShouldBe(System.Net.HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RoleDto>();
    }

    /// <summary>Creates a user holding exactly the given permissions and returns a client signed in as them.</summary>
    public static async Task<HttpClient> UserWithPermissionsAsync(this TmsApiFactory factory, HttpClient admin, params string[] permissions)
    {
        var role = await admin.CreateRoleAsync(permissions);
        var email = UniqueEmail("limited");
        await admin.CreateUserAsync(email, [role.Id]);
        return await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, StrongPassword);
    }
}
