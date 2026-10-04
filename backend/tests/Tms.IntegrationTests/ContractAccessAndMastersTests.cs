using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Transporters.Domain;
using static Tms.IntegrationTests.Infrastructure.ContractApiData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ContractAccessAndMastersTests(TmsApiFactory factory)
{
    [Fact]
    public async Task Permissions_gate_reading_and_writing()
    {
        using var admin = await factory.AdminAsync();
        using var viewer = await factory.UserWithPermissionsAsync(admin, ContractPermissions.Read);
        using var nobody = await factory.UserWithPermissionsAsync(admin);
        var transporter = await ActiveTransporterAsync(admin);
        var contract = await CreateAsync(admin, NewContract(transporter.Id));

        (await viewer.GetAsync($"/api/v1/contracts/{contract.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.GetAsync("/api/v1/zones")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.PostJsonAsync("/api/v1/contracts", NewContract(transporter.Id))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostAsync($"/api/v1/contracts/{contract.Summary.Id}/submit", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await viewer.PostJsonAsync("/api/v1/diesel-prices", new AddDieselPriceRequest("Delhi", Today, 90m))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await nobody.GetAsync("/api/v1/contracts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.PostJsonAsync("/api/v1/freight/quote", Quote("Goa", "A", "Goa", "B"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Vendor_portal_users_cannot_see_commercial_terms_even_with_a_contracts_permission()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var contract = await CreateAsync(admin, NewContract(transporter.Id));
        var role = await admin.CreateExternalRoleAsync(TransporterPermissions.SelfManage);
        var email = ApiExtensions.UniqueEmail("vendor");
        (await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(email, "Vendor", ApiExtensions.StrongPassword, UserType.Transporter, [role.Id], transporter.Id)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        using var vendor = await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword);

        (await vendor.GetAsync("/api/v1/contracts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendor.GetAsync($"/api/v1/contracts/{contract.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendor.PostJsonAsync("/api/v1/freight/quote", Quote("Goa", "A", "Goa", "B"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // A staff-only permission cannot be placed in an external role to work around this.
        var leak = await admin.PostJsonAsync("/api/v1/roles", new Tms.Modules.Platform.Application.Roles.SaveRoleRequest("Leak", null, [ContractPermissions.Read], null, RoleAudience.External));
        (await leak.ProblemCodeAsync()).ShouldBe("roles.permission_not_external");
    }

    [Fact]
    public async Task Zones_and_diesel_prices_reject_duplicates_and_bad_input()
    {
        using var admin = await factory.AdminAsync();
        var code = $"DUP{Guid.NewGuid():N}"[..8];
        var members = new[] { new ZoneMember("Goa", null) };

        var created = await admin.PostJsonAsync("/api/v1/zones", new SaveZoneRequest(code, "First", members, null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var zone = await created.ReadAsync<ZoneDto>();
        (await admin.PostJsonAsync("/api/v1/zones", new SaveZoneRequest(code.ToLowerInvariant(), "Again", members, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PostJsonAsync("/api/v1/zones", new SaveZoneRequest("bad code!", "X", members, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await admin.PostJsonAsync("/api/v1/zones", new SaveZoneRequest($"E{Guid.NewGuid():N}"[..8], "Empty", [], null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var edited = await admin.PutJsonAsync($"/api/v1/zones/{zone.Id}", new SaveZoneRequest(code, "Renamed", [new ZoneMember("Goa", null), new ZoneMember("Kerala", null)], zone.Version));
        (await edited.ReadAsync<ZoneDto>()).Members.Count.ShouldBe(2);
        (await admin.PutJsonAsync($"/api/v1/zones/{zone.Id}", new SaveZoneRequest(code, "Stale", members, zone.Version))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var region = $"R{Guid.NewGuid():N}"[..8];
        (await admin.PostJsonAsync("/api/v1/diesel-prices", new AddDieselPriceRequest(region, Today, 91.234m))).StatusCode.ShouldBe(HttpStatusCode.Created);
        var duplicate = await admin.PostJsonAsync("/api/v1/diesel-prices", new AddDieselPriceRequest(region, Today, 92m));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ProblemCodeAsync()).ShouldBe("diesel.price_exists");
        (await admin.PostJsonAsync("/api/v1/diesel-prices", new AddDieselPriceRequest(region, Today.AddDays(1), 0m))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var listed = await (await admin.GetAsync($"/api/v1/diesel-prices?region={region}")).ReadAsync<List<DieselPriceDto>>();
        listed.ShouldHaveSingleItem().PricePerLitre.ShouldBe(91.23m);
    }

    [Fact]
    public async Task The_document_repository_stores_checks_and_returns_files_faithfully()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var contract = await CreateAsync(admin, NewContract(transporter.Id));

        async Task<HttpResponseMessage> Upload(byte[] content, string name, string title)
        {
            using var form = new MultipartFormDataContent { { new StringContent("SignedContract"), "kind" }, { new StringContent(title), "title" } };
            var file = new ByteArrayContent(content);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", name);
            return await admin.PostAsync($"/api/v1/contracts/{contract.Summary.Id}/documents", form);
        }

        (await Upload(System.Text.Encoding.ASCII.GetBytes("MZ not a pdf"), "evil.pdf", "Signed")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Upload(TransporterTestData.Pdf, "x.pdf", " ")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var uploaded = await Upload(TransporterTestData.Pdf, "../../signed agreement.pdf", "Signed agreement");
        uploaded.StatusCode.ShouldBe(HttpStatusCode.Created, await uploaded.Content.ReadAsStringAsync());
        var document = await uploaded.ReadAsync<ContractDocumentDto>();
        document.FileName.ShouldBe("signed agreement.pdf");

        (await (await admin.GetAsync($"/api/v1/contracts/{contract.Summary.Id}/documents")).ReadAsync<List<ContractDocumentDto>>()).ShouldHaveSingleItem();
        var download = await admin.GetAsync($"/api/v1/contract-documents/{document.Id}/file");
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(TransporterTestData.Pdf);
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");

        (await admin.DeleteAsync($"/api/v1/contract-documents/{document.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/contract-documents/{document.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Listing_filters_by_status_type_transporter_and_search()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var draft = await CreateAsync(admin, NewContract(transporter.Id, ContractType.Ptl));

        async Task<IReadOnlyList<ContractSummaryDto>> List(string query) =>
            (await (await admin.GetAsync($"/api/v1/contracts?{query}&pageSize=200")).ReadAsync<Tms.SharedKernel.Paging.PagedResult<ContractSummaryDto>>()).Items;

        (await List($"transporterId={transporter.Id}&status=Draft")).ShouldContain(c => c.Id == draft.Summary.Id);
        (await List($"transporterId={transporter.Id}&status=Active")).ShouldNotContain(c => c.Id == draft.Summary.Id);
        (await List($"transporterId={transporter.Id}&type=Ptl")).ShouldContain(c => c.Id == draft.Summary.Id);
        (await List($"transporterId={transporter.Id}&type=Ftl")).ShouldNotContain(c => c.Id == draft.Summary.Id);
        (await List($"search={draft.Summary.Number}")).ShouldContain(c => c.Id == draft.Summary.Id);
    }

    [Fact]
    public async Task Contract_users_get_vehicle_types_without_needing_transporter_permissions()
    {
        using var admin = await factory.AdminAsync();
        using var contractsOnly = await factory.UserWithPermissionsAsync(admin, ContractPermissions.Read);
        using var nobody = await factory.UserWithPermissionsAsync(admin);

        (await contractsOnly.GetAsync("/api/v1/vehicle-types")).StatusCode.ShouldBe(HttpStatusCode.Forbidden, "the transporter-side list stays permission-gated");
        var types = await (await contractsOnly.GetAsync("/api/v1/contracts/lookups/vehicle-types")).ReadAsync<List<Tms.SharedKernel.Contracts.VehicleTypeInfo>>();

        types.Select(t => t.Code).ShouldContain("TRUCK_32FT_MXL");
        types.Select(t => t.PayloadKg).ShouldBe(types.Select(t => t.PayloadKg).Order(), "smallest first");
        (await nobody.GetAsync("/api/v1/contracts/lookups/vehicle-types")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
