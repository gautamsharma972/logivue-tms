using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Platform;
using Tms.Modules.Platform.Application;
using Tms.Modules.Platform.Application.Auth;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AccountLifecycleTests(TmsApiFactory factory)
{
    private const string NewPassword = "Fresh#Passw0rd99";

    private CapturingEmailSender Mailbox => factory.Services.GetRequiredService<CapturingEmailSender>();

    private static async Task<UserDto> NewUserAsync(HttpClient admin, bool requirePasswordChange = false)
    {
        var email = ApiExtensions.UniqueEmail("acct");
        var response = await admin.PostJsonAsync("/api/v1/users",
            new CreateUserRequest(email, "Account Tester", ApiExtensions.StrongPassword, UserType.Internal, [], null, requirePasswordChange));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<UserDto>();
    }

    private static Task<HttpResponseMessage> TryLoginAsync(HttpClient client, string email, string password) =>
        client.PostJsonAsync("/api/v1/auth/login", new LoginRequest("DEMO", email, password));

    [Fact]
    public async Task Forgot_password_mails_a_link_for_real_accounts_and_answers_identically_for_unknown_ones()
    {
        using var admin = await factory.AdminAsync();
        var user = await NewUserAsync(admin);
        var client = factory.CreateClient();

        var real = await client.PostJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("DEMO", user.Email));
        var ghost = await client.PostJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("DEMO", "nobody-here@demo.tms"));
        var wrongTenant = await client.PostJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("NOSUCH", user.Email));

        new[] { real, ghost, wrongTenant }.ShouldAllBe(r => r.StatusCode == HttpStatusCode.Accepted);
        Mailbox.LatestTokenFor(user.Email).ShouldNotBeNullOrWhiteSpace();
        Mailbox.LatestTokenFor("nobody-here@demo.tms").ShouldBeNull();
    }

    [Fact]
    public async Task A_reset_link_sets_the_password_once_unlocks_the_account_and_ends_old_sessions()
    {
        using var admin = await factory.AdminAsync();
        var user = await NewUserAsync(admin);
        var (_, oldSession) = await factory.LoginWithCookieAsync("DEMO", user.Email, ApiExtensions.StrongPassword);
        var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await TryLoginAsync(client, user.Email, "wrong-password-1"); // lock the account
        }

        await client.PostJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("DEMO", user.Email));
        var token = Mailbox.LatestTokenFor(user.Email)!;

        var reset = await client.PostJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, NewPassword));
        reset.StatusCode.ShouldBe(HttpStatusCode.NoContent, await reset.Content.ReadAsStringAsync());

        (await TryLoginAsync(client, user.Email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TryLoginAsync(client, user.Email, ApiExtensions.StrongPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await factory.RefreshWithAsync(oldSession)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var again = await client.PostJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, "Another#Passw0rd1"));
        again.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await again.ProblemCodeAsync()).ShouldBe("auth.reset_token_invalid");
    }

    [Fact]
    public async Task Requesting_a_new_link_invalidates_the_previous_one()
    {
        using var admin = await factory.AdminAsync();
        var user = await NewUserAsync(admin);
        var client = factory.CreateClient();
        await client.PostJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("DEMO", user.Email));
        var first = Mailbox.LatestTokenFor(user.Email)!;
        await client.PostJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("DEMO", user.Email));
        var second = Mailbox.LatestTokenFor(user.Email)!;

        second.ShouldNotBe(first);
        (await client.PostJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(first, NewPassword))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.PostJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(second, NewPassword))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Reset_rejects_garbage_tokens_and_weak_passwords()
    {
        using var admin = await factory.AdminAsync();
        var user = await NewUserAsync(admin);
        var client = factory.CreateClient();
        await client.PostJsonAsync("/api/v1/auth/forgot-password", new ForgotPasswordRequest("DEMO", user.Email));

        var garbage = await client.PostJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest("not-a-real-token", NewPassword));
        (await garbage.ProblemCodeAsync()).ShouldBe("auth.reset_token_invalid");

        var weak = await client.PostJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(Mailbox.LatestTokenFor(user.Email)!, "short"));
        weak.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await weak.Content.ReadAsStringAsync()).ShouldContain("\"newPassword\"");
        (await TryLoginAsync(client, user.Email, ApiExtensions.StrongPassword)).StatusCode.ShouldBe(HttpStatusCode.OK, "a rejected reset leaves the password alone");
    }

    [Fact]
    public async Task Changing_your_password_checks_the_current_one_and_ends_other_sessions()
    {
        using var admin = await factory.AdminAsync();
        var user = await NewUserAsync(admin);
        var (auth, otherDevice) = await factory.LoginWithCookieAsync("DEMO", user.Email, ApiExtensions.StrongPassword);
        using var me = factory.CreateClient();
        me.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var wrong = await me.PostJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest("not-my-password", NewPassword));
        wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await wrong.Content.ReadAsStringAsync()).ShouldContain("\"currentPassword\"");

        var same = await me.PostJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest(ApiExtensions.StrongPassword, ApiExtensions.StrongPassword));
        (await same.ProblemCodeAsync()).ShouldBe("auth.password_unchanged");

        var changed = await me.PostJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest(ApiExtensions.StrongPassword, NewPassword));
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        changed.RefreshCookieFrom().ShouldNotBeNull("this device continues with a fresh session");

        (await TryLoginAsync(factory.CreateClient(), user.Email, NewPassword)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await TryLoginAsync(factory.CreateClient(), user.Email, ApiExtensions.StrongPassword)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await factory.RefreshWithAsync(otherDevice)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_admin_set_password_must_be_replaced_before_anything_else_works()
    {
        using var admin = await factory.AdminAsync();
        var user = await NewUserAsync(admin, requirePasswordChange: true);
        using var session = await factory.SignedInAsync("DEMO", user.Email, ApiExtensions.StrongPassword);

        var profile = await (await session.GetAsync("/api/v1/auth/me")).ReadAsync<UserProfile>();
        profile.MustChangePassword.ShouldBeTrue();

        var blocked = await session.GetAsync("/api/v1/users/lookup");
        blocked.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await blocked.ProblemCodeAsync()).ShouldBe("auth.password_change_required");

        var changed = await session.PostJsonAsync("/api/v1/auth/change-password", new ChangePasswordRequest(ApiExtensions.StrongPassword, NewPassword));
        var fresh = await changed.ReadAsync<AuthResponse>();
        fresh.User.MustChangePassword.ShouldBeFalse();

        session.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", fresh.AccessToken);
        (await session.GetAsync("/api/v1/users/lookup")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Provisioning_a_tenant_yields_a_working_administrator_through_a_single_use_invitation()
    {
        var code = $"NEW{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var email = $"owner@{code.ToLowerInvariant()}.example";

        var provisioned = await factory.Services.ProvisionTenantAsync(new ProvisionTenantRequest(code, "Brand New Logistics", email, "Olivia Owner"));

        provisioned.IsSuccess.ShouldBeTrue(provisioned.IsFailure ? provisioned.Error.Description : string.Empty);
        var token = Mailbox.LatestTokenFor(email).ShouldNotBeNull("the invitation is also emailed");
        provisioned.Value.InviteLink.ShouldContain(Uri.EscapeDataString(token));

        var client = factory.CreateClient();
        (await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest(code, email, NewPassword))).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized, "no usable password exists until the invitation is accepted");

        (await client.PostJsonAsync("/api/v1/auth/reset-password", new ResetPasswordRequest(token, NewPassword))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var login = await client.PostJsonAsync("/api/v1/auth/login", new LoginRequest(code, email, NewPassword));
        var auth = await login.ReadAsync<AuthResponse>();
        auth.User.Roles.ShouldBe(["Administrator"]);
        auth.User.TenantName.ShouldBe("Brand New Logistics");
        auth.User.Permissions.ShouldContain("users.manage");

        // The new organisation is isolated from the others.
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var users = await (await client.GetAsync("/api/v1/users")).ReadAsync<Tms.SharedKernel.Paging.PagedResult<UserDto>>();
        users.Items.ShouldHaveSingleItem().Email.ShouldBe(email);
    }

    [Fact]
    public async Task Provisioning_rejects_duplicate_codes_and_bad_input()
    {
        var duplicate = await factory.Services.ProvisionTenantAsync(new ProvisionTenantRequest("demo", "Again", "x@y.example", "X"));
        duplicate.Error.Code.ShouldBe("tenants.code_exists");

        (await factory.Services.ProvisionTenantAsync(new ProvisionTenantRequest("bad code!", "N", "x@y.example", "X"))).Error.Code.ShouldBe("tenants.code_invalid");
        (await factory.Services.ProvisionTenantAsync(new ProvisionTenantRequest("OKCODE1", "N", "not-an-email", "X"))).Error.Code.ShouldBe("tenants.details_invalid");
    }
}
