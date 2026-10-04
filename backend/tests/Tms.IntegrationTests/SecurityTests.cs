using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Platform.Application.Audit;
using Tms.Modules.Platform.Application.Roles;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class SecurityTests(TmsApiFactory factory)
{
    [Fact]
    public async Task Tenants_cannot_see_each_others_data()
    {
        using var demo = await factory.AdminAsync();
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        var demoUser = await demo.CreateUserAsync();

        var acmeUsers = await (await acme.GetAsync("/api/v1/users?pageSize=200")).ReadAsync<PagedResult<UserDto>>();
        acmeUsers.Items.ShouldNotContain(u => u.Id == demoUser.Id);
        acmeUsers.Items.ShouldAllBe(u => u.Email.EndsWith("@acme.tms", StringComparison.Ordinal));

        (await acme.GetAsync($"/api/v1/users/{demoUser.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var acmeAudit = await (await acme.GetAsync("/api/v1/audit-logs?pageSize=200")).ReadAsync<PagedResult<AuditLogDto>>();
        acmeAudit.Items.ShouldNotContain(a => a.EntityId == demoUser.Id.ToString());
    }

    [Fact]
    public async Task A_tenant_cannot_assign_another_tenants_role()
    {
        using var demo = await factory.AdminAsync();
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        var demoRole = await demo.CreateRoleAsync("users.read");

        var response = await acme.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            "x@acme.tms", "X", ApiExtensions.StrongPassword, UserType.Internal, [demoRole.Id]));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ProblemCodeAsync()).ShouldBe("users.invalid_roles");
    }

    [Fact]
    public async Task Permissions_gate_each_endpoint()
    {
        using var admin = await factory.AdminAsync();
        using var viewer = await factory.UserWithPermissionsAsync(admin, "users.read");
        using var nobody = await factory.UserWithPermissionsAsync(admin);

        (await viewer.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            ApiExtensions.UniqueEmail(), "N", ApiExtensions.StrongPassword, UserType.Internal, []))).StatusCode
            .ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.GetAsync("/api/v1/audit-logs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync("/api/v1/users")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_user_manager_cannot_escalate_privileges_by_assigning_roles_they_do_not_hold()
    {
        using var admin = await factory.AdminAsync();
        using var manager = await factory.UserWithPermissionsAsync(admin, "users.manage", "users.read", "roles.read", "roles.manage");
        var roles = await (await manager.GetAsync("/api/v1/roles")).ReadAsync<List<RoleDto>>();
        var administrator = roles.Single(r => r.IsSystem);

        var assign = await manager.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            ApiExtensions.UniqueEmail(), "Sneaky", ApiExtensions.StrongPassword, UserType.Internal, [administrator.Id]));
        assign.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await assign.ProblemCodeAsync()).ShouldBe("users.role_escalation");

        var grant = await manager.PostJsonAsync("/api/v1/roles",
            new SaveRoleRequest("Super", null, ["audit.read"], null)); // manager does not hold audit.read
        grant.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await grant.ProblemCodeAsync()).ShouldBe("roles.permission_escalation");
    }

    [Fact]
    public async Task System_roles_are_immutable_and_custom_role_names_are_unique()
    {
        using var admin = await factory.AdminAsync();
        var roles = await (await admin.GetAsync("/api/v1/roles")).ReadAsync<List<RoleDto>>();
        var system = roles.Single(r => r.IsSystem);

        var edit = await admin.PutJsonAsync($"/api/v1/roles/{system.Id}",
            new SaveRoleRequest("Renamed", null, system.Permissions, system.Version));
        edit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await edit.ProblemCodeAsync()).ShouldBe("roles.system_immutable");

        var custom = await admin.CreateRoleAsync("users.read");
        var duplicate = await admin.PostJsonAsync("/api/v1/roles", new SaveRoleRequest(custom.Name, null, ["users.read"], null));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Unknown_permissions_are_rejected()
    {
        using var admin = await factory.AdminAsync();

        var response = await admin.PostJsonAsync("/api/v1/roles", new SaveRoleRequest("Bogus", null, ["not.a.permission"], null));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ProblemCodeAsync()).ShouldBe("roles.unknown_permission");
    }
}
