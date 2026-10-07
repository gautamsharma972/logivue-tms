using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Platform.Application.Roles;
using Tms.Modules.Platform.Application.Users;
using Tms.Modules.Platform.Domain;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.TransporterTestData;

namespace Tms.IntegrationTests;

/// <summary>A vendor-portal user must reach their own company and nothing else.</summary>
[Collection(ApiCollection.Name)]
public class VendorPortalTests(TmsApiFactory factory)
{
    private async Task<HttpClient> VendorForAsync(HttpClient admin, TransporterDto transporter, params string[] permissions)
    {
        var role = await admin.CreateExternalRoleAsync(permissions);
        var email = ApiExtensions.UniqueEmail("vendor");
        var response = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            email, "Vendor User", ApiExtensions.StrongPassword, UserType.Transporter, [role.Id], transporter.Id));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await factory.SignedInAsync(TmsApiFactory.DemoTenant, email, ApiExtensions.StrongPassword);
    }

    [Fact]
    public async Task Transporter_and_driver_users_must_be_linked_to_an_existing_transporter_and_staff_must_not_be()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        async Task<HttpResponseMessage> Create(UserType type, Guid? transporterId) => await admin.PostJsonAsync("/api/v1/users",
            new CreateUserRequest(ApiExtensions.UniqueEmail(), "X", ApiExtensions.StrongPassword, type, [], transporterId));

        var unlinked = await Create(UserType.Transporter, null);
        unlinked.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unlinked.ProblemCodeAsync()).ShouldBe("users.transporter_link");
        (await Create(UserType.Internal, transporter.Id)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Create(UserType.Driver, null)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await Create(UserType.Driver, transporter.Id)).StatusCode.ShouldBe(HttpStatusCode.Created);
        var unknown = await Create(UserType.Transporter, Guid.NewGuid());
        (await unknown.ProblemCodeAsync()).ShouldBe("users.transporter_unknown");

        var linked = await Create(UserType.Transporter, transporter.Id);
        linked.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await linked.ReadAsync<UserDto>()).TransporterId.ShouldBe(transporter.Id);
    }

    [Fact]
    public async Task A_vendor_sees_and_reaches_only_their_own_company()
    {
        using var admin = await factory.AdminAsync();
        var mine = await CreateAsync(admin);
        var theirs = await CreateAsync(admin);
        using var vendor = await VendorForAsync(admin, mine, TransporterPermissions.SelfManage);

        var list = await (await vendor.GetAsync("/api/v1/transporters?pageSize=200")).ReadAsync<PagedResult<TransporterSummaryDto>>();
        list.Items.ShouldHaveSingleItem().Id.ShouldBe(mine.Id);

        (await vendor.GetAsync($"/api/v1/transporters/{mine.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await vendor.GetAsync($"/api/v1/transporters/{theirs.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vendor.GetAsync($"/api/v1/transporters/{theirs.Id}/vehicles")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vendor.GetAsync($"/api/v1/transporters/{theirs.Id}/documents")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vendor.GetAsync("/api/v1/transporters/lookup")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_vendor_cannot_do_internal_work_or_touch_bank_or_identity()
    {
        using var admin = await factory.AdminAsync();
        var mine = await CreateReadyAsync(admin);
        using var vendor = await VendorForAsync(admin, mine, TransporterPermissions.SelfManage);

        (await vendor.PostJsonAsync("/api/v1/transporters", NewRequest())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendor.PostAsync($"/api/v1/transporters/{mine.Id}/submit", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vendor.PostJsonAsync($"/api/v1/transporters/{mine.Id}/suspend", new SuspendRequest("x"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vendor.PutJsonAsync($"/api/v1/transporters/{mine.Id}/bank", new SaveBankRequest("Evil", "999999999999", "HDFC0009999", "HDFC", mine.Version)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Contact details can be edited; attempts to rewrite identity are silently ignored, not honoured.
        var edit = await vendor.PutJsonAsync($"/api/v1/transporters/{mine.Id}", new SaveTransporterRequest(
            "Hijacked Name Ltd", null, "ZZZZZ9999Z", null, "New Contact", "9123456780", mine.Email,
            "New Address", null, "Mumbai", "Maharashtra", "400001", ["Ftl"], mine.Version));
        edit.StatusCode.ShouldBe(HttpStatusCode.OK, await edit.Content.ReadAsStringAsync());
        var after = await edit.ReadAsync<TransporterDto>();
        after.LegalName.ShouldBe(mine.LegalName);
        after.Pan.ShouldBe(mine.Pan);
        after.Gstin.ShouldBe(mine.Gstin);
        after.ContactPerson.ShouldBe("New Contact");
        after.Address.City.ShouldBe("Mumbai");
    }

    [Fact]
    public async Task A_vendor_maintains_their_own_fleet_drivers_and_documents_only()
    {
        using var admin = await factory.AdminAsync();
        var mine = await CreateAsync(admin);
        var theirs = await CreateAsync(admin);
        using var vendor = await VendorForAsync(admin, mine, TransporterPermissions.SelfManage);
        var type = (await (await vendor.GetAsync("/api/v1/vehicle-types")).ReadAsync<List<VehicleTypeDto>>()).First();
        string Plate() => $"GJ{Random.Shared.Next(10, 99)}AB{Random.Shared.Next(1000, 9999)}";

        var own = await vendor.PostJsonAsync($"/api/v1/transporters/{mine.Id}/vehicles", new SaveVehicleRequest(Plate(), type.Id, VehicleOwnership.Owned, null, null, true, null));
        own.StatusCode.ShouldBe(HttpStatusCode.Created, await own.Content.ReadAsStringAsync());
        var vehicle = await own.ReadAsync<VehicleDto>();
        (await vendor.PostJsonAsync($"/api/v1/transporters/{theirs.Id}/vehicles", new SaveVehicleRequest(Plate(), type.Id, VehicleOwnership.Owned, null, null, true, null)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        (await UploadAsync(vendor, mine.Id, OwnerKind.Vehicle, vehicle.Id, DocumentKind.RegistrationCertificate)).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await UploadAsync(vendor, theirs.Id, OwnerKind.Transporter, theirs.Id, DocumentKind.PanCard)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Another company's vehicle can't be edited, and another company's document can't be read or deleted.
        var theirVehicle = await (await admin.PostJsonAsync($"/api/v1/transporters/{theirs.Id}/vehicles",
            new SaveVehicleRequest(Plate(), type.Id, VehicleOwnership.Owned, null, null, true, null))).ReadAsync<VehicleDto>();
        (await vendor.PutJsonAsync($"/api/v1/vehicles/{theirVehicle.Id}", new SaveVehicleRequest(theirVehicle.RegistrationNumber, type.Id, VehicleOwnership.Owned, "Mine now", null, true, theirVehicle.Version)))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);
        await UploadOkAsync(admin, theirs.Id, OwnerKind.Transporter, theirs.Id, DocumentKind.PanCard);
        var theirDoc = (await (await admin.GetAsync($"/api/v1/transporters/{theirs.Id}/documents")).ReadAsync<List<DocumentDto>>()).Single();
        (await vendor.GetAsync($"/api/v1/documents/{theirDoc.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await vendor.DeleteAsync($"/api/v1/documents/{theirDoc.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_vendor_account_without_the_portal_permission_can_do_nothing()
    {
        using var admin = await factory.AdminAsync();
        var mine = await CreateAsync(admin);
        using var vendor = await VendorForAsync(admin, mine); // role with no permissions

        (await vendor.GetAsync($"/api/v1/transporters/{mine.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await vendor.GetAsync("/api/v1/transporters")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_vendors_compliance_report_only_shows_their_own_papers()
    {
        using var admin = await factory.AdminAsync();
        var mine = await CreateAsync(admin);
        var theirs = await CreateAsync(admin);
        using var vendor = await VendorForAsync(admin, mine, TransporterPermissions.SelfManage);
        var soon = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5);
        async Task<Guid> DriverFor(TransporterDto t)
        {
            var d = await (await admin.PostJsonAsync($"/api/v1/transporters/{t.Id}/drivers",
                new SaveDriverRequest("Driver", "9876543210", null, true, null))).ReadAsync<DriverDto>();
            await UploadOkAsync(admin, t.Id, OwnerKind.Driver, d.Id, DocumentKind.DrivingLicense, soon);
            return d.Id;
        }

        var mineDriver = await DriverFor(mine);
        var theirDriver = await DriverFor(theirs);

        var report = await (await vendor.GetAsync("/api/v1/transporters/compliance?pageSize=200")).ReadAsync<PagedResult<ComplianceItemDto>>();
        report.Items.ShouldContain(i => i.Document.OwnerId == mineDriver);
        report.Items.ShouldNotContain(i => i.Document.OwnerId == theirDriver);
    }

    [Fact]
    public async Task Vendor_and_staff_roles_cannot_be_mixed()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        var staffRole = await admin.CreateRoleAsync(TransporterPermissions.Read);
        var vendorRole = await admin.CreateExternalRoleAsync(TransporterPermissions.SelfManage);

        // A staff-only permission cannot be placed in an external role at all.
        var leaky = await admin.PostJsonAsync("/api/v1/roles", new SaveRoleRequest("Leaky", null, ["users.manage"], null, RoleAudience.External));
        leaky.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await leaky.ProblemCodeAsync()).ShouldBe("roles.permission_not_external");

        // A vendor account cannot be given a staff role…
        var vendorWithStaffRole = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            ApiExtensions.UniqueEmail(), "V", ApiExtensions.StrongPassword, UserType.Transporter, [staffRole.Id], transporter.Id));
        vendorWithStaffRole.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await vendorWithStaffRole.ProblemCodeAsync()).ShouldBe("users.role_audience");

        // …and staff cannot be given a vendor role.
        var staffWithVendorRole = await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            ApiExtensions.UniqueEmail(), "S", ApiExtensions.StrongPassword, UserType.Internal, [vendorRole.Id]));
        (await staffWithVendorRole.ProblemCodeAsync()).ShouldBe("users.role_audience");

        // The same rule applies when roles are edited later.
        var vendor = await (await admin.PostJsonAsync("/api/v1/users", new CreateUserRequest(
            ApiExtensions.UniqueEmail(), "V2", ApiExtensions.StrongPassword, UserType.Transporter, [vendorRole.Id], transporter.Id))).ReadAsync<UserDto>();
        var promote = await admin.PutJsonAsync($"/api/v1/users/{vendor.Id}", new UpdateUserRequest("V2", true, [staffRole.Id], vendor.Version));
        (await promote.ProblemCodeAsync()).ShouldBe("users.role_audience");

        vendorRole.Audience.ShouldBe(RoleAudience.External);
    }
}
