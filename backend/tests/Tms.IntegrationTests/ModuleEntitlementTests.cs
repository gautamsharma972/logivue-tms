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
        (await client.GetAsync("/api/v1/ops/lrs")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await client.PostJsonAsync("/api/v1/ops/invoices", new { })).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // reading is implied, issuing invoices is not

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
