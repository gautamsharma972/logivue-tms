using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Application.Fleet;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Driver-level compliance and assignment, plus API-level authentication and role enforcement.</summary>
public class DriverAndAuthorisationTests : IClassFixture<TestHost>
{
    private const string AllRoles = "Transport Admin,Transport Manager,Transport Executive,Compliance User,Finance User,Operations User";
    private const string VendorWriterRoles = "Transporter Admin,Transporter Operations User";
    private const string VendorViewerRoles = "Transporter Viewer";

    private readonly TestHost _host;
    private readonly HttpClient _admin;

    public DriverAndAuthorisationTests(TestHost host)
    {
        _host = host;
        _host.EnsureTransporterSchemaAsync().GetAwaiter().GetResult();
        _admin = Internal(AllRoles);
    }

    private HttpClient Internal(string roles, string user = "ops")
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, user);
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, roles);
        return client;
    }

    private HttpClient Vendor(long transporterId, string roles = VendorWriterRoles)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, $"vendor-{transporterId}");
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.TransporterHeader, transporterId.ToString());
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, roles);
        return client;
    }

    private static async Task<ApiError> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiError>(TestHost.Json))!;

    // ---------- drivers ----------

    [Fact]
    public async Task Driver_is_registered_once_per_licence_number()
    {
        var seeded = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Driver Co", 42000m));
        var licence = $"DL{Guid.NewGuid():N}"[..12];
        var first = await _admin.PostAsJsonAsync($"/api/v1/transporters/{seeded}/drivers",
            new SaveDriverRequest("Amara Okafor", "+91 98200 11111", licence), TestHost.Json);
        var duplicate = await _admin.PostAsJsonAsync($"/api/v1/transporters/{seeded}/drivers",
            new SaveDriverRequest("Other Name", "+91 98200 22222", licence.ToLowerInvariant()), TestHost.Json);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ErrorOf(duplicate)).Code.Should().Be("DRIVER_LICENCE_DUPLICATE");
    }

    [Fact]
    public async Task Expired_driver_licence_is_reported_and_blocks_only_that_driver()
    {
        var seeded = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Licence Co", 42000m));
        var driver = await CreateDriverAsync(seeded, "+91 98200 33333");
        await SeedLicenceAsync(seeded, driver.Id, expiry: new DateOnly(2020, 1, 1));

        var report = await _admin.GetFromJsonAsync<ComplianceReportDto>($"/api/v1/transporters/{seeded}/compliance", TestHost.Json);

        report!.BlockedDriverIds.Should().Contain(driver.Id);
        report.AllocationBlocked.Should().BeFalse("an expired driver licence stops that driver, not the transporter");
        report.Overall.Should().Be(ComplianceOverallStatus.NonCompliant);
    }

    [Fact]
    public async Task Vehicle_assignment_is_refused_for_a_driver_with_an_expired_licence()
    {
        var lane = FixtureLane.Unique();
        var seeded = await TransporterFixtures.SeedDetailedAsync(_host, lane, new FixtureProfile("Assign Co", 42000m));
        var invitation = await AcceptedInvitationAsync(lane, seeded.Id);
        var driver = await CreateDriverAsync(seeded.Id, "+91 98200 44444");
        await SeedLicenceAsync(seeded.Id, driver.Id, expiry: new DateOnly(2020, 1, 1));

        var response = await Vendor(seeded.Id).PostAsJsonAsync($"/api/v1/vendor/loads/{invitation}/vehicle",
            new VehicleAssignmentRequest(seeded.VehicleRegistration, "Amara Okafor", "+91 98200 44444", null, null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(response)).Code.Should().Be("DRIVER_LICENCE_EXPIRED");
    }

    [Fact]
    public async Task Vehicle_assignment_succeeds_when_the_driver_licence_is_valid()
    {
        var lane = FixtureLane.Unique();
        var seeded = await TransporterFixtures.SeedDetailedAsync(_host, lane, new FixtureProfile("Valid Co", 42000m));
        var invitation = await AcceptedInvitationAsync(lane, seeded.Id);
        var driver = await CreateDriverAsync(seeded.Id, "+91 98200 55555");
        await SeedLicenceAsync(seeded.Id, driver.Id, expiry: new DateOnly(2030, 1, 1));

        var response = await Vendor(seeded.Id).PostAsJsonAsync($"/api/v1/vendor/loads/{invitation}/vehicle",
            new VehicleAssignmentRequest(seeded.VehicleRegistration, "Amara Okafor", "+91 98200 55555", null, null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------- authentication & roles ----------

    [Fact]
    public async Task Requests_without_a_signed_in_user_are_refused_with_401()
    {
        var anonymous = _host.CreateClient();

        var response = await anonymous.GetAsync("/api/v1/transporters");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ErrorOf(response)).Code.Should().Be("UNAUTHENTICATED");
    }

    [Fact]
    public async Task Health_and_module_info_are_open_to_anonymous_callers()
    {
        var anonymous = _host.CreateClient();

        (await anonymous.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/v1/transporter-management/info")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Users_without_a_master_data_role_cannot_change_transporter_data()
    {
        var compliance = Internal("Compliance User");
        var seeded = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Role Co", 42000m));

        var response = await compliance.PostAsJsonAsync($"/api/v1/transporters/{seeded}/drivers",
            new SaveDriverRequest("Blocked Driver", "+91 98200 66666", $"BL{Guid.NewGuid():N}"[..12]), TestHost.Json);
        var reads = await compliance.GetAsync($"/api/v1/transporters/{seeded}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorOf(response)).Code.Should().Be("FORBIDDEN");
        reads.StatusCode.Should().Be(HttpStatusCode.OK, "reads are open to any internal role");
    }

    [Fact]
    public async Task Vendor_viewers_can_read_but_cannot_accept_tenders()
    {
        var lane = FixtureLane.Unique();
        var seeded = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Viewer Co", 42000m));
        var invitation = await AcceptedInvitationAsync(lane, seeded, accept: false);
        var viewer = Vendor(seeded, VendorViewerRoles);

        var read = await viewer.GetAsync($"/api/v1/vendor/tenders/{invitation}");
        var accept = await viewer.PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation}/accept", new AcceptTenderRequest(null), TestHost.Json);

        read.StatusCode.Should().Be(HttpStatusCode.OK);
        accept.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorOf(accept)).Code.Should().Be("VENDOR_ROLE_REQUIRED");
    }

    [Fact]
    public async Task Vendor_users_with_no_transporter_role_cannot_use_the_portal()
    {
        var seeded = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("No Role Co", 42000m));

        var response = await Vendor(seeded, roles: "").GetAsync("/api/v1/vendor/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorOf(response)).Code.Should().Be("VENDOR_ROLE_REQUIRED");
    }

    // ---------- helpers ----------

    private async Task<DriverDto> CreateDriverAsync(long transporterId, string mobile)
    {
        var response = await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/drivers",
            new SaveDriverRequest("Amara Okafor", mobile, $"DL{Guid.NewGuid():N}"[..12]), TestHost.Json);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<DriverDto>(TestHost.Json))!;
    }

    private async Task SeedLicenceAsync(long transporterId, long driverId, DateOnly expiry) =>
        await _host.WithTransporterDbAsync(async db =>
        {
            db.TransporterDocuments.Add(new TransporterDocument
            {
                TransporterId = transporterId, DriverId = driverId, DocumentTypeId = 9, ExpiryDate = expiry,
                VerificationStatus = DocumentVerificationStatus.Verified, VerifiedBy = "seed", VerifiedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return true;
        });

    /// <summary>Tenders a direct load to the transporter and (optionally) accepts it. Returns the invitation id.</summary>
    private async Task<long> AcceptedInvitationAsync(FixtureLane lane, long transporterId, bool accept = true)
    {
        var pickup = DateTime.UtcNow.AddDays(5).Date.AddHours(8);
        var response = await _admin.PostAsJsonAsync("/api/v1/tenders", new CreateTenderRequest(TenderType.Direct,
            $"LD-{Guid.NewGuid():N}"[..12], lane.Origin, lane.Destination, "FTL", lane.VehicleType, 12000m, null,
            pickup, pickup.AddDays(2), DateTime.UtcNow.AddHours(4), 42000m, "INR", [transporterId], null), TestHost.Json);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invitation = (await response.Content.ReadFromJsonAsync<List<TenderInvitationDto>>(TestHost.Json))!.Single();
        await _admin.PostAsync($"/api/v1/tenders/{invitation.Id}/send", null);
        if (accept)
        {
            await Vendor(transporterId).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/accept", new AcceptTenderRequest(null), TestHost.Json);
        }

        return invitation.Id;
    }
}
