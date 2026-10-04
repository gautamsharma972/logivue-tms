using System.Net;
using System.Net.Http.Headers;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Platform.Application.Auth;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AuthTests(TmsApiFactory factory)
{
    [Fact]
    public async Task Login_with_valid_credentials_returns_tokens_and_profile()
    {
        var auth = await factory.CreateClient()
            .LoginAsync("demo", TmsApiFactory.DemoAdminEmail, TmsApiFactory.DemoAdminPassword); // tenant code is case-insensitive

        auth.AccessToken.ShouldNotBeNullOrWhiteSpace();
        auth.User.TenantCode.ShouldBe("DEMO");
        auth.User.Roles.ShouldContain("Administrator");
        auth.User.Permissions.ShouldContain("users.manage");
    }

    [Theory]
    [InlineData("DEMO", "admin@demo.tms", "wrong-password")]
    [InlineData("DEMO", "nobody@demo.tms", "whatever-1234")]
    [InlineData("NOSUCH", "admin@demo.tms", "whatever-1234")]
    public async Task Failed_logins_are_indistinguishable(string tenant, string email, string password)
    {
        var response = await factory.CreateClient().PostJsonAsync("/api/v1/auth/login", new LoginRequest(tenant, email, password));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ProblemCodeAsync()).ShouldBe("auth.invalid_credentials");
    }

    [Fact]
    public async Task A_user_is_locked_out_after_five_failed_attempts_even_with_the_right_password()
    {
        using var admin = await factory.AdminAsync();
        var user = await admin.CreateUserAsync();
        var client = factory.CreateClient();

        for (var i = 0; i < 5; i++)
        {
            (await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest("DEMO", user.Email, "bad-password-1"))).StatusCode
                .ShouldBe(HttpStatusCode.Unauthorized);
        }

        var response = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest("DEMO", user.Email, ApiExtensions.StrongPassword));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.ProblemCodeAsync()).ShouldBe("auth.locked_out");
    }

    [Fact]
    public async Task The_refresh_token_travels_only_in_an_HttpOnly_cookie_never_in_the_body()
    {
        using var client = factory.CreateClient();
        var response = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest("DEMO", TmsApiFactory.DemoAdminEmail, TmsApiFactory.DemoAdminPassword));

        (await response.Content.ReadAsStringAsync()).ShouldNotContain("refreshToken", Case.Insensitive);
        var cookie = response.Headers.GetValues("Set-Cookie").Single(v => v.StartsWith("tms_refresh=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/api/v1/auth", Case.Insensitive);
    }

    [Fact]
    public async Task Refresh_rotates_the_token_and_reuse_of_an_old_one_revokes_the_whole_session()
    {
        var (_, first) = await factory.LoginWithCookieAsync(TmsApiFactory.DemoTenant, TmsApiFactory.DemoAdminEmail, TmsApiFactory.DemoAdminPassword);

        var rotated = await factory.RefreshWithAsync(first);
        rotated.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = rotated.RefreshCookieFrom().ShouldNotBeNull();
        second.ShouldNotBe(first);
        (await rotated.ReadAsync<AuthResponse>()).AccessToken.ShouldNotBeNullOrWhiteSpace();

        var replay = await factory.RefreshWithAsync(first);
        replay.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await replay.ProblemCodeAsync()).ShouldBe("auth.refresh_token_reuse");

        // The legitimate holder of the newest token is signed out too: the chain is considered compromised.
        (await factory.RefreshWithAsync(second)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_without_a_cookie_or_without_the_CSRF_header_is_refused()
    {
        var (_, cookie) = await factory.LoginWithCookieAsync(TmsApiFactory.DemoTenant, TmsApiFactory.DemoAdminEmail, TmsApiFactory.DemoAdminPassword);

        (await factory.RefreshWithAsync(null)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var forged = await factory.RefreshWithAsync(cookie, csrfHeader: false);
        forged.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await forged.ProblemCodeAsync()).ShouldBe("request.csrf_header_missing");
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_clears_the_cookie()
    {
        var (_, cookie) = await factory.LoginWithCookieAsync(TmsApiFactory.DemoTenant, TmsApiFactory.DemoAdminEmail, TmsApiFactory.DemoAdminPassword);
        using var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { HandleCookies = false });
        var logout = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logout.Headers.Add("Cookie", $"tms_refresh={cookie}");
        logout.Headers.Add("X-TMS-Client", "tests");

        var response = await client.SendAsync(logout);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        response.Headers.GetValues("Set-Cookie").Single().ShouldContain("expires=", Case.Insensitive);
        (await factory.RefreshWithAsync(cookie)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Me_requires_a_valid_token_and_returns_the_profile()
    {
        (await factory.CreateClient().GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var garbage = factory.CreateClient();
        garbage.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");
        (await garbage.GetAsync("/api/v1/auth/me")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        using var admin = await factory.AdminAsync();
        var me = await (await admin.GetAsync("/api/v1/auth/me")).ReadAsync<UserProfile>();
        me.Email.ShouldBe(TmsApiFactory.DemoAdminEmail);
    }

    [Fact]
    public async Task Health_endpoints_report_ready()
    {
        var client = factory.CreateClient();

        (await client.GetAsync("/health/live")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Responses_carry_a_correlation_id_and_security_headers()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "test-correlation-1");

        var response = await factory.CreateClient().SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").ShouldBe(["test-correlation-1"]);
        response.Headers.GetValues("X-Content-Type-Options").ShouldBe(["nosniff"]);
    }
}
