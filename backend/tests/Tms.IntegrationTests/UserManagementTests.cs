using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Platform.Application.Audit;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
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
}

internal static class HttpClientListExtensions
{
    public static async Task<IReadOnlyList<T>> GetFromJsonListAsync<T>(this HttpClient client, string url) =>
        (await (await client.GetAsync(url)).ReadAsync<PagedResult<T>>()).Items;
}
