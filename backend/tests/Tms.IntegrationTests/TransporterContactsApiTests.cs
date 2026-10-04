using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Transporters.Application.Performance;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransporterContactsApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task A_transporter_keeps_extra_contacts_with_one_primary_and_the_code_of_a_branch_is_unique()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var first = await (await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/contacts", new SaveContactRequest("Anil", "Dispatch head", null, "9876543210", "Operations", true))).ReadAsync<ContactDto>();
        var second = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/contacts", new SaveContactRequest("Meena", null, "meena@x.example", null, "Accounts", true));
        second.StatusCode.ShouldBe(HttpStatusCode.Created, await second.Content.ReadAsStringAsync());

        var list = await (await s.Admin.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/contacts")).ReadAsync<List<ContactDto>>();
        list.Single(c => c.IsPrimary).Name.ShouldBe("Meena"); // making her primary made Anil an ordinary contact
        list.Single(c => c.Id == first.Id).IsPrimary.ShouldBeFalse();
        (await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/contacts", new SaveContactRequest("Nobody", null, null, null, "Operations", false))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var branch = await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/branches", new SaveBranchRequest("pnq-1", "Pune hub", "MIDC", "Pune", "Maharashtra", 18.5, 73.8, null, null));
        branch.StatusCode.ShouldBe(HttpStatusCode.Created, await branch.Content.ReadAsStringAsync());
        var dto = await branch.ReadAsync<BranchDto>();
        dto.Code.ShouldBe("PNQ-1");
        (await s.Admin.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/branches", new SaveBranchRequest("PNQ-1", "Other", null, null, null, null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var updated = await s.Admin.PutJsonAsync($"/api/v1/branches/{dto.Id}", new SaveBranchRequest("PNQ-1", "Pune main hub", "MIDC", "Pune", "Maharashtra", 18.5, 73.8, null, null, false, dto.Version));
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await updated.ReadAsync<BranchDto>()).IsActive.ShouldBeFalse();
        (await s.Admin.PutJsonAsync($"/api/v1/branches/{dto.Id}", new SaveBranchRequest("PNQ-1", "Stale", null, null, null, null, null, null, null, true, dto.Version))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_vendor_maintains_its_own_contacts_and_branches_and_cannot_touch_anothers()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        using var other = await ShipmentScenario.CreateAsync(factory);
        var vendorRole = await s.Admin.CreateExternalRoleAsync("transporters.self.manage");
        var email = ApiExtensions.UniqueEmail("vendor");
        await s.Admin.PostJsonAsync("/api/v1/users", new Tms.Modules.Platform.Application.Users.CreateUserRequest(email, "Vendor Owner", ApiExtensions.StrongPassword, Tms.Modules.Platform.Domain.UserType.Transporter, [vendorRole.Id], s.Transporter.Id));
        using var vendor = await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword);

        (await vendor.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/contacts", new SaveContactRequest("Own desk", null, null, "9876543210", "Operations", false))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await vendor.PostJsonAsync($"/api/v1/transporters/{s.Transporter.Id}/branches", new SaveBranchRequest("OWN-1", "Own depot", null, null, null, null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await vendor.PostJsonAsync($"/api/v1/transporters/{other.Transporter.Id}/contacts", new SaveContactRequest("Intruder", null, null, "9876543210", "Operations", false))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vendor.GetAsync($"/api/v1/transporters/{other.Transporter.Id}/branches")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Vendor.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/contacts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // a dispatcher without the company-profile permission
    }
}
