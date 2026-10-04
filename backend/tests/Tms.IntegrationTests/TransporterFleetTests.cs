using System.Net;
using System.Text;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.TransporterTestData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransporterFleetTests(TmsApiFactory factory)
{
    private static string NewPlate() => $"MH{Random.Shared.Next(10, 99)}{(char)('A' + Random.Shared.Next(26))}{(char)('A' + Random.Shared.Next(26))}{Random.Shared.Next(1000, 9999)}";

    private static async Task<VehicleTypeDto> AnyVehicleTypeAsync(HttpClient client) =>
        (await (await client.GetAsync("/api/v1/vehicle-types")).ReadAsync<List<VehicleTypeDto>>()).First(t => t.Code == "TRUCK_32FT_MXL");

    private static async Task<VehicleDto> AddVehicleAsync(HttpClient client, Guid transporterId, string? plate = null)
    {
        var type = await AnyVehicleTypeAsync(client);
        var response = await client.PostJsonAsync($"/api/v1/transporters/{transporterId}/vehicles",
            new SaveVehicleRequest(plate ?? NewPlate(), type.Id, VehicleOwnership.Owned, "Tata", 2022, true, null));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<VehicleDto>();
    }

    [Fact]
    public async Task Standard_vehicle_types_are_provided_to_every_tenant_and_can_be_extended()
    {
        using var admin = await factory.AdminAsync();
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);

        var demo = await (await admin.GetAsync("/api/v1/vehicle-types")).ReadAsync<List<VehicleTypeDto>>();
        var other = await (await acme.GetAsync("/api/v1/vehicle-types")).ReadAsync<List<VehicleTypeDto>>();
        demo.Select(t => t.Code).ShouldContain("TRUCK_32FT_MXL");
        other.Select(t => t.Id).Intersect(demo.Select(t => t.Id)).ShouldBeEmpty("each tenant owns its own copy");

        var created = await admin.PostJsonAsync("/api/v1/vehicle-types", new SaveVehicleTypeRequest($"custom_{Guid.NewGuid():N}"[..14], "Custom 8 T", 8000, 40m, true, null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await admin.PostJsonAsync("/api/v1/vehicle-types", new SaveVehicleTypeRequest("TRUCK_32FT_MXL", "Dup", 1, null, true, null)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Vehicles_are_validated_and_registration_numbers_are_unique_across_the_tenant()
    {
        using var admin = await factory.AdminAsync();
        var a = await CreateAsync(admin);
        var b = await CreateAsync(admin);
        var plate = NewPlate();
        var vehicle = await AddVehicleAsync(admin, a.Id, plate.ToLowerInvariant().Insert(2, " "));
        vehicle.RegistrationNumber.ShouldBe(plate);

        var type = await AnyVehicleTypeAsync(admin);
        var duplicate = await admin.PostJsonAsync($"/api/v1/transporters/{b.Id}/vehicles",
            new SaveVehicleRequest(plate, type.Id, VehicleOwnership.Attached, null, null, true, null));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ProblemCodeAsync()).ShouldBe("vehicles.registration_exists");

        var invalid = await admin.PostJsonAsync($"/api/v1/transporters/{b.Id}/vehicles",
            new SaveVehicleRequest("NOT A PLATE", Guid.NewGuid(), VehicleOwnership.Owned, null, null, true, null));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.ProblemCodeAsync()).ShouldBe("vehicles.type_unknown");
    }

    [Fact]
    public async Task A_vehicle_is_compliant_only_while_its_papers_are_in_date()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        var vehicle = await AddVehicleAsync(admin, transporter.Id);
        vehicle.Compliance.Status.ShouldBe(ComplianceStatus.NonCompliant);
        vehicle.Compliance.Issues.ShouldContain("Insurance missing");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        async Task Upload(DocumentKind kind, DateOnly? expires) => await UploadOkAsync(admin, transporter.Id, OwnerKind.Vehicle, vehicle.Id, kind, expires);
        async Task<VehicleDto> Reload() =>
            (await (await admin.GetAsync($"/api/v1/transporters/{transporter.Id}/vehicles")).ReadAsync<PagedResult<VehicleDto>>()).Items.Single(v => v.Id == vehicle.Id);

        await Upload(DocumentKind.RegistrationCertificate, null);
        await Upload(DocumentKind.Insurance, today.AddMonths(6));
        await Upload(DocumentKind.Fitness, today.AddMonths(6));
        await Upload(DocumentKind.Permit, today.AddMonths(6));
        (await Reload()).Compliance.Status.ShouldBe(ComplianceStatus.Compliant);

        await Upload(DocumentKind.Insurance, today.AddDays(10)); // renewal uploaded with a short validity
        var soon = await Reload();
        soon.Compliance.Status.ShouldBe(ComplianceStatus.ExpiringSoon);

        await Upload(DocumentKind.Insurance, today.AddDays(-1));
        var expired = await Reload();
        expired.Compliance.Status.ShouldBe(ComplianceStatus.NonCompliant);
        expired.Compliance.Issues.ShouldHaveSingleItem().ShouldStartWith("Insurance expired on");

        await Upload(DocumentKind.Insurance, today.AddYears(1)); // renewed: the lapsed one is superseded
        (await Reload()).Compliance.Status.ShouldBe(ComplianceStatus.Compliant);
    }

    [Fact]
    public async Task Drivers_are_validated_and_licences_are_unique()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        var license = $"MH12{Random.Shared.Next(2010, 2024)}{Random.Shared.Next(1000000, 9999999)}";

        var created = await admin.PostJsonAsync($"/api/v1/transporters/{transporter.Id}/drivers", new SaveDriverRequest("Ramesh Yadav", "+91 98765 43210", license, true, null));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var driver = await created.ReadAsync<DriverDto>();
        driver.Phone.ShouldBe("9876543210");
        driver.Compliance.Status.ShouldBe(ComplianceStatus.NonCompliant);

        (await admin.PostJsonAsync($"/api/v1/transporters/{transporter.Id}/drivers", new SaveDriverRequest("Other", "9876543211", license, true, null)))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PostJsonAsync($"/api/v1/transporters/{transporter.Id}/drivers", new SaveDriverRequest("X", "12", null, true, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        await UploadOkAsync(admin, transporter.Id, OwnerKind.Driver, driver.Id, DocumentKind.DrivingLicense, DateOnly.FromDateTime(DateTime.UtcNow).AddYears(3));
        var listed = await (await admin.GetAsync($"/api/v1/transporters/{transporter.Id}/drivers")).ReadAsync<PagedResult<DriverDto>>();
        listed.Items.Single().Compliance.Status.ShouldBe(ComplianceStatus.Compliant);
    }

    [Fact]
    public async Task Updating_a_vehicle_needs_the_current_version()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        var vehicle = await AddVehicleAsync(admin, transporter.Id);
        var request = new SaveVehicleRequest(vehicle.RegistrationNumber, vehicle.VehicleTypeId, VehicleOwnership.Attached, "Ashok Leyland", 2023, false, vehicle.Version);

        var updated = await admin.PutJsonAsync($"/api/v1/vehicles/{vehicle.Id}", request);
        updated.StatusCode.ShouldBe(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await updated.ReadAsync<VehicleDto>()).IsActive.ShouldBeFalse();

        (await admin.PutJsonAsync($"/api/v1/vehicles/{vehicle.Id}", request)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Uploads_are_checked_by_content_not_by_name_or_declared_type()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        var exe = Encoding.ASCII.GetBytes("MZ\u0090\u0000 this is not a pdf, whatever the extension says");

        var disguised = await UploadAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.PanCard, exe, "pan.pdf");
        disguised.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await disguised.ProblemCodeAsync()).ShouldBe("documents.file_type");

        var huge = new byte[(10 * 1024 * 1024) + 16];
        Pdf.CopyTo(huge, 0);
        var tooBig = await UploadAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.PanCard, huge);
        tooBig.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await tooBig.ProblemCodeAsync()).ShouldBe("documents.file_too_large");

        (await UploadAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.PanCard, Png, "pan.png"))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        // A path-traversal file name is reduced to its last segment.
        var traversal = await UploadAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.MsmeCertificate, fileName: "../../etc/passwd.pdf");
        (await traversal.ReadAsync<DocumentDto>()).FileName.ShouldBe("passwd.pdf");
    }

    [Fact]
    public async Task Documents_must_suit_their_owner_and_carry_an_expiry_where_required()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        var vehicle = await AddVehicleAsync(admin, transporter.Id);

        var wrongKind = await UploadAsync(admin, transporter.Id, OwnerKind.Vehicle, vehicle.Id, DocumentKind.PanCard);
        (await wrongKind.Content.ReadAsStringAsync()).ShouldContain("\"kind\"");

        var noExpiry = await UploadAsync(admin, transporter.Id, OwnerKind.Vehicle, vehicle.Id, DocumentKind.Insurance);
        (await noExpiry.Content.ReadAsStringAsync()).ShouldContain("\"expiresOn\"");

        var strangerOwner = await UploadAsync(admin, transporter.Id, OwnerKind.Vehicle, Guid.NewGuid(), DocumentKind.RegistrationCertificate);
        strangerOwner.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var otherTransporter = await CreateAsync(admin);
        var crossOwner = await UploadAsync(admin, otherTransporter.Id, OwnerKind.Vehicle, vehicle.Id, DocumentKind.RegistrationCertificate);
        crossOwner.StatusCode.ShouldBe(HttpStatusCode.NotFound); // the vehicle belongs to a different transporter
    }

    [Fact]
    public async Task Documents_download_exactly_as_uploaded_and_replacements_supersede_without_losing_history()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        await UploadOkAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.PanCard);
        await UploadAsync(admin, transporter.Id, OwnerKind.Transporter, transporter.Id, DocumentKind.PanCard, Png, "newer.png");

        var current = await (await admin.GetAsync($"/api/v1/transporters/{transporter.Id}/documents")).ReadAsync<List<DocumentDto>>();
        var pan = current.ShouldHaveSingleItem();
        pan.FileName.ShouldBe("newer.png");
        var history = await (await admin.GetAsync($"/api/v1/transporters/{transporter.Id}/documents?includeSuperseded=true")).ReadAsync<List<DocumentDto>>();
        history.Count.ShouldBe(2);
        history.Count(d => d.IsCurrent).ShouldBe(1);

        var download = await admin.GetAsync($"/api/v1/documents/{pan.Id}/file");
        download.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).ShouldBe(Png);
        download.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        download.Content.Headers.ContentDisposition!.DispositionType.ShouldBe("attachment");

        (await admin.DeleteAsync($"/api/v1/documents/{pan.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await admin.GetAsync($"/api/v1/documents/{pan.Id}/file")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_compliance_report_lists_expired_and_expiring_papers_oldest_first()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        var vehicle = await AddVehicleAsync(admin, transporter.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await UploadOkAsync(admin, transporter.Id, OwnerKind.Vehicle, vehicle.Id, DocumentKind.Insurance, today.AddDays(-3));
        await UploadOkAsync(admin, transporter.Id, OwnerKind.Vehicle, vehicle.Id, DocumentKind.Fitness, today.AddDays(12));
        await UploadOkAsync(admin, transporter.Id, OwnerKind.Vehicle, vehicle.Id, DocumentKind.Permit, today.AddYears(1));

        var report = await (await admin.GetAsync("/api/v1/transporters/compliance?withinDays=30&pageSize=200")).ReadAsync<PagedResult<ComplianceItemDto>>();
        var mine = report.Items.Where(i => i.Document.OwnerId == vehicle.Id).ToList();

        mine.Select(i => i.Document.Kind).ShouldBe([DocumentKind.Insurance, DocumentKind.Fitness]);
        mine[0].Document.ExpiryStatus.ShouldBe(ExpiryStatus.Expired);
        mine[1].Document.ExpiryStatus.ShouldBe(ExpiryStatus.ExpiringSoon);
        mine[0].OwnerLabel.ShouldBe(vehicle.RegistrationNumber);
        mine[0].TransporterName.ShouldBe(transporter.LegalName);
        report.Items.Select(i => i.Document.ExpiresOn).ShouldBeInOrder();
    }
}
