using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Platform.Application;
using Tms.Modules.Platform.Application.Tenants;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.Modules.Platform.Infrastructure.Security;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ModuleEntitlementTests(TmsApiFactory factory)
{
    private const string Password = "Module@12345678";

    private async Task<(string Tenant, string Email)> SeedAsync(bool platform, IEnumerable<string>? modules)
    {
        var code = $"{(platform ? "PLT" : "MOD")}{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var email = $"admin@{code.ToLowerInvariant()}.example";
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var catalog = scope.ServiceProvider.GetRequiredService<PermissionCatalog>();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var tenant = Tenant.Create(code, $"Org {code}", modules, platform);
        var admin = Role.Create(tenant.Id, Role.AdministratorName, null, catalog.Codes, isSystem: true);
        db.AddRange(tenant, admin, User.Create(tenant.Id, email, "Admin", passwords.Hash(Password), UserType.Internal, [admin]));
        await db.SaveChangesAsync();
        return (code, email);
    }

    [Fact]
    public async Task A_role_that_can_dispatch_can_also_see_operations_records()
    {
        var code = $"IMP{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var email = $"planner@{code.ToLowerInvariant()}.example";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
            var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
            var tenant = Tenant.Create(code, $"Org {code}", ["transport-ops"]);
            var role = Role.Create(tenant.Id, "Planner", null, ["ops.dispatch", "ops.arrival.override"], isSystem: false);
            db.AddRange(tenant, role, User.Create(tenant.Id, email, "Planner", passwords.Hash(Password), UserType.Internal, [role]));
            await db.SaveChangesAsync();
        }

        using var client = await factory.SignedInAsync(code, email, Password);
        var me = await (await client.GetAsync("/api/v1/auth/me")).ReadAsync<Tms.Modules.Platform.Application.Auth.UserProfile>();
        me.Permissions.ShouldContain("ops.read");
        me.Permissions.ShouldContain("ops.pod.upload"); // a dispatcher can also attach the proof of delivery
        (await client.GetAsync("/api/v1/ops/lrs")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostJsonAsync("/api/v1/ops/invoices", new { })).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // reading is implied, issuing invoices is not

        // Cross billing (a price typed on the LR instead of the agreement) needs its own permission.
        var line = new Tms.Modules.TransportOps.Application.LrLineRequest(null, null, 1, "PKG", 10, null, null, 1);
        var cross = new Tms.Modules.TransportOps.Application.CreateLrRequest(null, Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), null, null, [line], null,
            Tms.Modules.TransportOps.Domain.MovementType.CrossBilling, new Tms.Modules.TransportOps.Application.CrossBillingRequest(Tms.Modules.TransportOps.Domain.CrossBillingMode.Lumpsum, 5000, null, null));
        (await client.PostJsonAsync("/api/v1/ops/lrs", cross)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Operations and money are separate: a planner never sees rates, billing, invoices or the billing figures on the dashboard.
        me.Permissions.ShouldNotContain("ops.billing.read");
        foreach (var url in new[] { "/api/v1/ops/agreements", "/api/v1/ops/billing", "/api/v1/ops/invoices", "/api/v1/ops/receivables" })
        {
            (await client.GetAsync(url)).StatusCode.ShouldBe(HttpStatusCode.Forbidden, url);
        }

        var dashboard = await (await client.GetAsync("/api/v1/ops/dashboard")).ReadAsync<Tms.Modules.TransportOps.Application.DashboardDto>();
        dashboard.Outstanding.ShouldBeNull();
        dashboard.BillingOnHold.ShouldBeNull();
    }

    [Fact]
    public async Task An_organisation_only_holds_the_permissions_of_the_modules_it_was_given()
    {
        var (code, email) = await SeedAsync(false, ["transport-ops", "reports"]);
        using var client = await factory.SignedInAsync(code, email, Password);
        var me = await (await client.GetAsync("/api/v1/auth/me")).ReadAsync<Tms.Modules.Platform.Application.Auth.UserProfile>();

        me.Modules.ShouldBe(["reports", "transport-ops"]);
        me.Permissions.ShouldContain("ops.read");
        me.Permissions.ShouldContain("reports.operations"); // its reports follow the module
        me.Permissions.ShouldContain("users.manage"); // core administration is always there
        me.Permissions.ShouldNotContain("tracking.read");
        me.Permissions.ShouldNotContain("transporters.read");
        me.Permissions.ShouldNotContain("reports.tracking");
        me.Permissions.ShouldNotContain("platform.tenants.manage");
        (await client.GetAsync("/api/v1/ops/customers")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.GetAsync("/api/v1/transporters")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_platform_administrator_can_edit_an_organisation_and_cap_its_users_and_locations()
    {
        var (code, email) = await SeedAsync(false, ["transport-ops"]);
        var (platformCode, platformEmail) = await SeedAsync(true, null);
        using var platform = await factory.SignedInAsync(platformCode, platformEmail, Password);
        using var customer = await factory.SignedInAsync(code, email, Password);
        var target = (await (await platform.GetAsync("/api/v1/platform/tenants")).ReadAsync<List<TenantDto>>()).Single(t => t.Code == code);

        (await platform.PutJsonAsync($"/api/v1/platform/tenants/{target.Id}", new UpdateTenantRequest("Renamed Org", ["transport-ops"], 0, 5))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // at least one user
        var edited = await (await platform.PutJsonAsync($"/api/v1/platform/tenants/{target.Id}", new UpdateTenantRequest("Renamed Org", ["transport-ops"], 2, 1))).ReadAsync<TenantDto>();
        (edited.Name, edited.Code, edited.MaxUsers, edited.MaxLocations).ShouldBe(("Renamed Org", code, 2, 1));
        (await customer.PutJsonAsync($"/api/v1/platform/tenants/{target.Id}", new UpdateTenantRequest("x", [], null, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Users: the administrator is one of the two allowed.
        var role = await customer.CreateRoleAsync("ops.read");
        await customer.CreateUserAsync(ApiExtensions.UniqueEmail("second"), [role.Id]);
        var third = await customer.PostJsonAsync("/api/v1/users", new Tms.Modules.Platform.Application.Users.CreateUserRequest(ApiExtensions.UniqueEmail("third"), "Third", ApiExtensions.StrongPassword, Tms.Modules.Platform.Domain.UserType.Internal, [role.Id]));
        third.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await third.ProblemCodeAsync()).ShouldBe("tenants.user_limit");

        // Locations: one is allowed.
        Tms.Modules.TransportOps.Application.SaveLocationRequest Location(string c) => new(c, "Place", Tms.Modules.TransportOps.Domain.LocationKind.Cfa, null, "Gate", "Mumbai", "Maharashtra", null, null, null, 200, null, Tms.Modules.TransportOps.Domain.ActiveStatus.Active);
        (await customer.PostJsonAsync("/api/v1/ops/locations", Location("L1"))).IsSuccessStatusCode.ShouldBeTrue();
        var second = await customer.PostJsonAsync("/api/v1/ops/locations", Location("L2"));
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ProblemCodeAsync()).ShouldBe("tenants.location_limit");

        // Raising the limit (or removing it) lets the organisation carry on.
        (await platform.PutJsonAsync($"/api/v1/platform/tenants/{target.Id}", new UpdateTenantRequest("Renamed Org", ["transport-ops"], null, null))).EnsureSuccessStatusCode();
        (await customer.PostJsonAsync("/api/v1/ops/locations", Location("L2"))).IsSuccessStatusCode.ShouldBeTrue();
    }

    [Fact]
    public async Task Approval_policies_cover_only_the_document_types_of_the_modules_an_organisation_has()
    {
        var (code, email) = await SeedAsync(false, ["transport-ops"]);
        using var opsOnly = await factory.SignedInAsync(code, email, Password);
        var types = (await (await opsOnly.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<Tms.Modules.Approvals.Application.PolicyDto>>()).Select(p => p.DocumentType).ToList();
        types.ShouldBe(["customer_invoice"]); // not transporter onboarding, contracts or claims
        var step = new Tms.Modules.Approvals.Application.PolicyStepDto("Manager", "ops.invoice.manage", null);
        (await opsOnly.PutJsonAsync("/api/v1/approvals/policies/freight_contract", new Tms.Modules.Approvals.Application.SavePolicyRequest(true, [step], null))).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var all = await factory.AdminAsync(); // an organisation with every module sees every type
        (await (await all.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<Tms.Modules.Approvals.Application.PolicyDto>>()).Select(p => p.DocumentType).ShouldContain("freight_contract");
    }

    [Fact]
    public async Task Only_platform_administrators_can_see_organisations_and_change_their_modules()
    {
        var (code, email) = await SeedAsync(false, ["tracking"]);
        var (platformCode, platformEmail) = await SeedAsync(true, null);

        using var customer = await factory.SignedInAsync(code, email, Password);
        (await customer.GetAsync("/api/v1/platform/tenants")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        using var platform = await factory.SignedInAsync(platformCode, platformEmail, Password);
        var tenants = await (await platform.GetAsync("/api/v1/platform/tenants")).ReadAsync<List<TenantDto>>();
        var target = tenants.Single(t => t.Code == code);
        target.Modules.ShouldBe(["tracking"]);

        (await platform.PutJsonAsync($"/api/v1/platform/tenants/{target.Id}/modules", new SetTenantModulesRequest(["bogus"]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var updated = await (await platform.PutJsonAsync($"/api/v1/platform/tenants/{target.Id}/modules", new SetTenantModulesRequest(["transport-ops", "reports"]))).ReadAsync<TenantDto>();
        updated.Modules.ShouldBe(["reports", "transport-ops"]);

        // The platform administrator can add an organisation with its first administrator and modules; a customer cannot.
        var newCode = $"NEW{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var body = new CreateTenantRequest(newCode, "Fresh Freight", "Fiona Founder", $"fiona@{newCode.ToLowerInvariant()}.example", ["transport-ops"]);
        (await customer.PostJsonAsync("/api/v1/platform/tenants", body)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await platform.PostJsonAsync("/api/v1/platform/tenants", body with { Modules = ["bogus"] })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var madeResponse = await platform.PostJsonAsync("/api/v1/platform/tenants", body);
        var raw = await madeResponse.Content.ReadAsStringAsync();
        madeResponse.StatusCode.ShouldBe(HttpStatusCode.OK, raw);
        raw.ShouldContain("inviteLink");
        var invitation = System.Text.Json.JsonDocument.Parse(raw).RootElement;
        invitation.GetProperty("code").GetString().ShouldBe(newCode);
        (await platform.PostJsonAsync("/api/v1/platform/tenants", body)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await (await platform.GetAsync("/api/v1/platform/tenants")).ReadAsync<List<TenantDto>>()).Single(t => t.Code == newCode).Modules.ShouldBe(["transport-ops"]);

        // The platform administrator can issue the organisation's first administrator a password link; a customer cannot, and the link really sets a working password.
        var created = (await (await platform.GetAsync("/api/v1/platform/tenants")).ReadAsync<List<TenantDto>>()).Single(t => t.Code == newCode);
        created.FirstAdminEmail.ShouldBe(body.AdminEmail);
        (await customer.PostAsync($"/api/v1/platform/tenants/{created.Id}/reset-admin-password", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var reset = await (await platform.PostAsync($"/api/v1/platform/tenants/{created.Id}/reset-admin-password", null)).ReadAsync<AdminPasswordResetDto>();
        reset.AdminEmail.ShouldBe(body.AdminEmail);
        var token = Uri.UnescapeDataString(reset.ResetLink.Split("token=")[1]);
        (reset.ExpiresAt - DateTimeOffset.UtcNow).ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(10));
        // Opened in a browser that is still signed in as the platform administrator, the link must still work.
        using var anonymous = factory.CreateClient();
        anonymous.DefaultRequestHeaders.Authorization = platform.DefaultRequestHeaders.Authorization;
        (await anonymous.PostJsonAsync("/api/v1/auth/reset-password", new Tms.Modules.Platform.Application.Auth.ResetPasswordRequest(token, "Fresh#Passw0rd!2026"))).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await anonymous.PostJsonAsync("/api/v1/auth/login", new Tms.Modules.Platform.Application.Auth.LoginRequest(newCode, body.AdminEmail, "Fresh#Passw0rd!2026"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await platform.PostAsync($"/api/v1/platform/tenants/{Guid.NewGuid()}/reset-admin-password", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // The platform organisation itself is not a customer.
        var self = tenants.Single(t => t.Code == platformCode);
        (await platform.PutJsonAsync($"/api/v1/platform/tenants/{self.Id}/modules", new SetTenantModulesRequest(["tracking"]))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // The customer's next profile reflects it.
        var me = await (await customer.GetAsync("/api/v1/auth/me")).ReadAsync<Tms.Modules.Platform.Application.Auth.UserProfile>();
        me.Permissions.ShouldContain("ops.read");
        me.Permissions.ShouldNotContain("tracking.read");
    }
}
