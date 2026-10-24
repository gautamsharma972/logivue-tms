using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Platform.Application.Audit;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Domain;
using static Tms.IntegrationTests.Infrastructure.TransporterTestData;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class UserManagementTests(TmsApiFactory factory)
{
    [Fact]
    public async Task Created_users_can_be_listed_searched_and_updated()
    {
        using var admin = await factory.AdminAsync();
        var email = ApiExtensions.UniqueEmail("search");
        var created = await admin.CreateUserAsync(email);

        created.Email.ShouldBe(email);
        created.Version.ShouldBe(0);

        var page = await (await admin.GetAsync($"/api/v1/users?search={email}")).ReadAsync<PagedResult<UserDto>>();
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(created.Id);

        var update = await admin.PutJsonAsync($"/api/v1/users/{created.Id}",
            new UpdateUserRequest("Renamed User", false, [], created.Version));
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await update.ReadAsync<UserDto>();
        updated.FullName.ShouldBe("Renamed User");
        updated.IsActive.ShouldBeFalse();
        updated.Version.ShouldBe(1);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        using var admin = await factory.AdminAsync();
        var email = ApiExtensions.UniqueEmail("dup");
        await admin.CreateUserAsync(email);

        var response = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            email.ToUpperInvariant(), "Dup", ApiExtensions.StrongPassword, UserType.Internal, []));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("users.email_taken");
    }

    [Fact]
    public async Task Weak_passwords_return_field_level_validation_errors()
    {
        using var admin = await factory.AdminAsync();

        var response = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            "not-an-email", "", "short", UserType.Internal, []));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldContain("\"password\"");
        body.ShouldContain("\"email\"");
        body.ShouldContain("\"fullName\"");
    }

    [Fact]
    public async Task Updating_with_a_stale_version_is_a_conflict()
    {
        using var admin = await factory.AdminAsync();
        var user = await admin.CreateUserAsync();

        (await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest("First Edit", true, [], user.Version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest("Second Edit", true, [], user.Version));

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ProblemCodeAsync()).ShouldBe("concurrency.conflict");
    }

    [Fact]
    public async Task An_administrator_cannot_deactivate_themselves()
    {
        using var admin = await factory.AdminAsync();
        var me = (await admin.GetFromJsonListAsync<UserDto>("/api/v1/users?search=" + TmsApiFactory.DemoAdminEmail)).Single();

        var response = await admin.PutJsonAsync($"/api/v1/users/{me.Id}",
            new UpdateUserRequest(me.FullName, false, me.Roles.Select(r => r.Id).ToList(), me.Version));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("users.cannot_deactivate_self");
    }

    [Fact]
    public async Task Changes_are_audited_without_leaking_secrets()
    {
        using var admin = await factory.AdminAsync();
        var user = await admin.CreateUserAsync();
        await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest("Audited Name", true, [], user.Version));

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityType=User&entityId={user.Id}"))
            .ReadAsync<PagedResult<AuditLogDto>>();

        logs.Items.Select(l => l.Action).ShouldBe(["Updated", "Created"]);
        logs.Items[0].UserName.ShouldBe("Demo Administrator");
        var update = logs.Items[0].Changes!.Value;
        update.GetProperty("FullName").GetProperty("old").GetString().ShouldBe("Test User");
        update.GetProperty("FullName").GetProperty("new").GetString().ShouldBe("Audited Name");

        var raw = string.Concat(logs.Items.Select(l => l.Changes?.ToString()));
        raw.ShouldNotContain("PasswordHash");
        raw.ShouldNotContain(ApiExtensions.StrongPassword);
    }

    [Fact]
    public async Task An_administrator_can_change_the_email_and_the_user_signs_in_with_the_new_one()
    {
        using var admin = await factory.AdminAsync();
        var oldEmail = ApiExtensions.UniqueEmail("old");
        var newEmail = ApiExtensions.UniqueEmail("new").ToUpperInvariant();
        var user = await admin.CreateUserAsync(oldEmail);

        var response = await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest(user.FullName, true, [], user.Version, Email: newEmail));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.ReadAsync<UserDto>()).Email.ShouldBe(newEmail.ToLowerInvariant());
        using var anonymous = factory.CreateClient();
        (await anonymous.PostJsonAsync("/api/v1/auth/login", new Tms.Modules.Platform.Application.Auth.LoginRequest(TmsApiFactory.DemoTenant, oldEmail, ApiExtensions.StrongPassword)))
            .StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await anonymous.LoginAsync(TmsApiFactory.DemoTenant, newEmail, ApiExtensions.StrongPassword)).AccessToken.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Changing_the_email_to_one_that_is_taken_is_a_conflict()
    {
        using var admin = await factory.AdminAsync();
        var taken = ApiExtensions.UniqueEmail("taken");
        await admin.CreateUserAsync(taken);
        var user = await admin.CreateUserAsync();

        var response = await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest(user.FullName, true, [], user.Version, Email: taken));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("users.email_taken");
    }

    [Fact]
    public async Task A_staff_user_can_be_moved_to_a_transporter_company_and_back()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        var staffRole = await admin.CreateRoleAsync(TransporterPermissions.Read);
        var vendorRole = await admin.CreateExternalRoleAsync(TransporterPermissions.SelfManage);
        var user = await admin.CreateUserAsync(roleIds: [staffRole.Id]);

        // The company is mandatory for a vendor account, and the roles must belong to the new audience.
        var noCompany = await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest(user.FullName, true, [vendorRole.Id], user.Version, Type: UserType.Transporter));
        (await noCompany.ProblemCodeAsync()).ShouldBe("users.transporter_link");
        var staffRoleOnVendor = await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest(user.FullName, true, [staffRole.Id], user.Version, Type: UserType.Transporter, TransporterId: transporter.Id));
        (await staffRoleOnVendor.ProblemCodeAsync()).ShouldBe("users.role_audience");

        var moved = await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest(user.FullName, true, [vendorRole.Id], user.Version, Type: UserType.Transporter, TransporterId: transporter.Id));
        moved.StatusCode.ShouldBe(HttpStatusCode.OK, await moved.Content.ReadAsStringAsync());
        var vendor = await moved.ReadAsync<UserDto>();
        vendor.Type.ShouldBe(UserType.Transporter);
        vendor.TransporterId.ShouldBe(transporter.Id);
        vendor.Roles.ShouldHaveSingleItem().Id.ShouldBe(vendorRole.Id);

        // Switching between vendor and driver keeps the company.
        var driver = await (await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest(user.FullName, true, [vendorRole.Id], vendor.Version, Type: UserType.Driver))).ReadAsync<UserDto>();
        driver.Type.ShouldBe(UserType.Driver);
        driver.TransporterId.ShouldBe(transporter.Id);

        // And back to staff: the company is removed.
        var back = await (await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest(user.FullName, true, [staffRole.Id], driver.Version, Type: UserType.Internal))).ReadAsync<UserDto>();
        back.Type.ShouldBe(UserType.Internal);
        back.TransporterId.ShouldBeNull();
    }

    [Fact]
    public async Task An_unknown_transporter_is_refused_and_staff_cannot_carry_one()
    {
        using var admin = await factory.AdminAsync();
        var staffRole = await admin.CreateRoleAsync(TransporterPermissions.Read);
        var vendorRole = await admin.CreateExternalRoleAsync(TransporterPermissions.SelfManage);
        var user = await admin.CreateUserAsync(roleIds: [staffRole.Id]);

        var unknown = await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest(user.FullName, true, [vendorRole.Id], user.Version, Type: UserType.Transporter, TransporterId: Guid.NewGuid()));
        (await unknown.ProblemCodeAsync()).ShouldBe("users.transporter_unknown");

        var staffWithCompany = await admin.PutJsonAsync($"/api/v1/users/{user.Id}", new UpdateUserRequest(user.FullName, true, [staffRole.Id], user.Version, TransporterId: Guid.NewGuid()));
        (await staffWithCompany.ProblemCodeAsync()).ShouldBe("users.transporter_link");
    }

    [Fact]
    public async Task An_administrator_cannot_change_the_type_of_their_own_account()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        var me = await (await admin.GetAsync("/api/v1/auth/me")).ReadAsync<Tms.Modules.Platform.Application.Auth.UserProfile>();
        var self = await (await admin.GetAsync($"/api/v1/users/{me.Id}")).ReadAsync<UserDto>();

        var response = await admin.PutJsonAsync($"/api/v1/users/{self.Id}", new UpdateUserRequest(self.FullName, true, self.Roles.Select(r => r.Id).ToList(), self.Version, Type: UserType.Transporter, TransporterId: transporter.Id));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("users.cannot_change_own_type");
    }
}

internal static class HttpClientListExtensions
{
    public static async Task<IReadOnlyList<T>> GetFromJsonListAsync<T>(this HttpClient client, string url) =>
        (await (await client.GetAsync(url)).ReadAsync<PagedResult<T>>()).Items;
}
