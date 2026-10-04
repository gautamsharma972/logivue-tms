using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Milestone 2: transporter master, contacts, branches, vehicles, lanes, capabilities, lookups and audit.</summary>
public class TransporterMasterApiTests : IClassFixture<TestHost>
{
    private const string BaseUrl = "/api/v1/transporters";
    private const string ValidGstin = "27AAPFU0939F1ZV";
    private const string ValidPan = "AAPFU0939F";

    private readonly TestHost _host;
    private const string AllRoles = "Transport Admin,Transport Manager,Transport Executive,Compliance User,Finance User,Operations User";

    private readonly HttpClient _client;

    public TransporterMasterApiTests(TestHost host)
    {
        _host = host;
        _host.EnsureTransporterSchemaAsync().GetAwaiter().GetResult();
        _client = host.CreateClient();
        _client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, "ops");
        _client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, AllRoles);
    }

    // ---------- helpers ----------

    private static string UniqueCode() => $"T{Guid.NewGuid():N}"[..10].ToUpperInvariant();

    private static CreateTransporterRequest NewTransporter(string? code = null, string? gstin = null, long? typeId = 1, string city = "Pune") =>
        new(code ?? UniqueCode(), "ABC Logistics Pvt Ltd", "ABC Logistics", typeId, "Private Limited",
            ValidPan, gstin, "U60200MH2015PTC000001", "Plot 12, MIDC", city, "Maharashtra", "India",
            "Asha Rao", "ops@abc.example", "+91 98200 00001", "https://abc.example");

    private async Task<TransporterDetail> CreateTransporterAsync(CreateTransporterRequest? request = null)
    {
        var response = await _client.PostAsJsonAsync(BaseUrl, request ?? NewTransporter(), TestHost.Json);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<TransporterDetail>(TestHost.Json))!;
    }

    private async Task<ApiError> ReadErrorAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiError>(TestHost.Json))!;

    // ---------- master data ----------

    [Fact]
    public async Task Create_transporter_starts_in_draft_with_uppercase_code()
    {
        var request = NewTransporter(code: "abc-tr-" + UniqueCode()[..4]);

        var created = await CreateTransporterAsync(request);

        created.Status.Should().Be(TransporterStatus.Draft);
        created.TransporterCode.Should().Be(request.TransporterCode.ToUpperInvariant());
        created.TransporterType.Should().Be("Full Truck Load");
    }

    [Fact]
    public async Task Create_with_duplicate_code_returns_409()
    {
        var request = NewTransporter();
        await CreateTransporterAsync(request);

        var response = await _client.PostAsJsonAsync(BaseUrl, request, TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(response)).Code.Should().Be("TRANSPORTER_CODE_EXISTS");
    }

    [Fact]
    public async Task Create_with_duplicate_gstin_returns_409()
    {
        var gstin = "29ABCDE1234F1Z5";
        await CreateTransporterAsync(NewTransporter(gstin: gstin));

        var response = await _client.PostAsJsonAsync(BaseUrl, NewTransporter(gstin: gstin), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(response)).Code.Should().Be("GSTIN_EXISTS");
    }

    [Fact]
    public async Task Create_with_malformed_gstin_returns_validation_error_on_that_field()
    {
        var response = await _client.PostAsJsonAsync(BaseUrl, NewTransporter(gstin: "NOT-A-GSTIN"), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var error = await ReadErrorAsync(response);
        error.Code.Should().Be("VALIDATION_ERROR");
        error.Details.Should().Contain(d => d.Field == nameof(CreateTransporterRequest.Gstin));
    }

    [Fact]
    public async Task Create_with_unknown_transporter_type_returns_422()
    {
        var response = await _client.PostAsJsonAsync(BaseUrl, NewTransporter(typeId: 999_999), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadErrorAsync(response)).Code.Should().Be("TRANSPORTER_TYPE_INVALID");
    }

    [Fact]
    public async Task Get_unknown_transporter_returns_404()
    {
        var response = await _client.GetAsync($"{BaseUrl}/987654321");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadErrorAsync(response)).Code.Should().Be("TRANSPORTER_NOT_FOUND");
    }

    [Fact]
    public async Task Update_changes_master_data_but_never_the_code()
    {
        var created = await CreateTransporterAsync();

        var update = new UpdateTransporterRequest("ABC Logistics Limited", "ABC Express", 3, "Partnership",
            ValidPan, ValidGstin, null, "Plot 99", "Mumbai", "Maharashtra", "India", "Asha Rao",
            "ops@abc.example", "+91 98200 00001", null);

        var response = await _client.PutAsJsonAsync($"{BaseUrl}/{created.Id}", update, TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await response.Content.ReadFromJsonAsync<TransporterDetail>(TestHost.Json))!;
        updated.LegalName.Should().Be("ABC Logistics Limited");
        updated.City.Should().Be("Mumbai");
        updated.TransporterCode.Should().Be(created.TransporterCode);
    }

    [Fact]
    public async Task Blacklisted_transporter_is_locked_for_edits()
    {
        var created = await CreateTransporterAsync();
        await _host.WithTransporterDbAsync(async db =>
        {
            var entity = await db.Transporters.SingleAsync(t => t.Id == created.Id);
            entity.Status = TransporterStatus.Blacklisted;
            await db.SaveChangesAsync();
            return true;
        });

        var update = new UpdateTransporterRequest("Renamed", null, 1, null, null, null, null, null, null, null, null, null, null, null, null);
        var response = await _client.PutAsJsonAsync($"{BaseUrl}/{created.Id}", update, TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadErrorAsync(response)).Code.Should().Be("TRANSPORTER_LOCKED");
    }

    // ---------- search ----------

    [Fact]
    public async Task Search_filters_by_city_and_pages_results()
    {
        var city = "City" + Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 3; i++)
        {
            await CreateTransporterAsync(NewTransporter(city: city));
        }

        var response = await _client.GetAsync($"{BaseUrl}?city={city}&page=1&pageSize=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<PagedResult<TransporterListItem>>(TestHost.Json))!;
        page.TotalCount.Should().Be(3);
        page.Items.Should().HaveCount(2);
        page.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task Search_matches_transporter_code_and_filters_by_status()
    {
        var created = await CreateTransporterAsync();

        var response = await _client.GetAsync($"{BaseUrl}?search={created.TransporterCode}&status=Draft");

        var page = (await response.Content.ReadFromJsonAsync<PagedResult<TransporterListItem>>(TestHost.Json))!;
        page.Items.Should().ContainSingle(i => i.Id == created.Id);
        page.Items.Should().OnlyContain(i => i.Status == TransporterStatus.Draft);
    }

    // ---------- contacts & branches ----------

    [Fact]
    public async Task Adding_a_second_primary_contact_demotes_the_first()
    {
        var transporter = await CreateTransporterAsync();
        var first = new SaveContactRequest("Asha Rao", "Ops Head", "asha@abc.example", "+91 98200 11111", "Operations", IsPrimary: true);
        var second = new SaveContactRequest("Vikram Shah", "Finance", "vikram@abc.example", "+91 98200 22222", "Finance", IsPrimary: true);

        await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/contacts", first, TestHost.Json);
        var response = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/contacts", second, TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await _client.GetFromJsonAsync<TransporterDetail>($"{BaseUrl}/{transporter.Id}", TestHost.Json);
        detail!.Contacts.Should().HaveCount(2);
        detail.Contacts.Single(c => c.IsPrimary).Name.Should().Be("Vikram Shah");
    }

    [Fact]
    public async Task Branch_code_is_unique_within_a_transporter()
    {
        var transporter = await CreateTransporterAsync();
        var branch = new SaveBranchRequest("PUN-01", "Pune Depot", "Chakan", "Pune", "Maharashtra", 18.7, 73.9, "Depot Lead", "+91 98200 33333");

        var first = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/branches", branch, TestHost.Json);
        var duplicate = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/branches", branch, TestHost.Json);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(duplicate)).Code.Should().Be("BRANCH_CODE_EXISTS");
    }

    [Fact]
    public async Task Branch_requires_both_coordinates_or_neither()
    {
        var transporter = await CreateTransporterAsync();
        var branch = new SaveBranchRequest("BLR-01", "Bengaluru Depot", null, "Bengaluru", null, 12.97, null, null, null);

        var response = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/branches", branch, TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- fleet ----------

    [Fact]
    public async Task Vehicle_registration_is_unique_regardless_of_spacing_and_case()
    {
        var transporter = await CreateTransporterAsync();
        var vehicle = new SaveVehicleRequest("MH12 AB1234", 1, 12000, 42, 12m, 2.4m, 2.6m,
            VehicleOwnershipType.Owned, VehicleAvailabilityStatus.Available, null);

        var first = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/vehicles", vehicle, TestHost.Json);
        var duplicate = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/vehicles",
            vehicle with { RegistrationNumber = "mh-12ab1234" }, TestHost.Json);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var stored = (await first.Content.ReadFromJsonAsync<VehicleDto>(TestHost.Json))!;
        stored.RegistrationNumber.Should().Be("MH12AB1234");
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(duplicate)).Code.Should().Be("VEHICLE_REGISTRATION_EXISTS");
    }

    [Fact]
    public async Task Vehicle_cannot_be_set_to_assigned_or_in_transit_manually()
    {
        var transporter = await CreateTransporterAsync();
        var vehicle = new SaveVehicleRequest("KA01 ZZ9999", 1, 7500, null, null, null, null,
            VehicleOwnershipType.Attached, VehicleAvailabilityStatus.Assigned, null);

        var response = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/vehicles", vehicle, TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadErrorAsync(response)).Code.Should().Be("VEHICLE_STATUS_NOT_MANUAL");
    }

    [Fact]
    public async Task Vehicle_list_filters_by_availability()
    {
        var transporter = await CreateTransporterAsync();
        await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/vehicles",
            new SaveVehicleRequest("GJ05 MT1111", 1, 9000, null, null, null, null, VehicleOwnershipType.Owned, VehicleAvailabilityStatus.Maintenance, null), TestHost.Json);
        await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/vehicles",
            new SaveVehicleRequest("GJ05 MT2222", 1, 9000, null, null, null, null, VehicleOwnershipType.Owned, VehicleAvailabilityStatus.Available, null), TestHost.Json);

        var response = await _client.GetAsync($"{BaseUrl}/{transporter.Id}/vehicles?availability=Maintenance");

        var page = (await response.Content.ReadFromJsonAsync<PagedResult<VehicleDto>>(TestHost.Json))!;
        page.Items.Should().ContainSingle().Which.RegistrationNumber.Should().Be("GJ05MT1111");
    }

    // ---------- coverage ----------

    [Fact]
    public async Task Overlapping_active_lane_returns_409()
    {
        var transporter = await CreateTransporterAsync();
        var lane = new SaveLaneRequest(10, 25, "FTL", 4, 1440, new DateTime(2026, 1, 1), null);
        await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/lanes", lane, TestHost.Json);

        var overlapping = lane with { EffectiveFrom = new DateTime(2026, 6, 1), EffectiveTo = new DateTime(2026, 9, 1) };
        var response = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/lanes", overlapping, TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(response)).Code.Should().Be("LANE_OVERLAP");
    }

    [Fact]
    public async Task Non_overlapping_lane_period_is_accepted()
    {
        var transporter = await CreateTransporterAsync();
        await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/lanes",
            new SaveLaneRequest(10, 25, "FTL", 4, 1440, new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)), TestHost.Json);

        var response = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/lanes",
            new SaveLaneRequest(10, 25, "FTL", 4, 1440, new DateTime(2026, 1, 1), null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Lane_destination_must_differ_from_origin()
    {
        var transporter = await CreateTransporterAsync();

        var response = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/lanes",
            new SaveLaneRequest(10, 10, "PTL", null, null, new DateTime(2026, 1, 1), null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadErrorAsync(response)).Details.Should().Contain(d => d.Field == nameof(SaveLaneRequest.DestinationLocationReference));
    }

    [Fact]
    public async Task Capability_can_be_added_once_and_removed_softly()
    {
        var transporter = await CreateTransporterAsync();
        var request = new AddCapabilityRequest(CapabilityTypeId: 1, EffectiveFrom: new DateTime(2026, 1, 1));

        var added = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/capabilities", request, TestHost.Json);
        var duplicate = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/capabilities", request, TestHost.Json);
        var capability = (await added.Content.ReadFromJsonAsync<CapabilityDto>(TestHost.Json))!;
        var removed = await _client.DeleteAsync($"{BaseUrl}/{transporter.Id}/capabilities/{capability.Id}");
        var removedAgain = await _client.DeleteAsync($"{BaseUrl}/{transporter.Id}/capabilities/{capability.Id}");

        added.StatusCode.Should().Be(HttpStatusCode.Created);
        capability.Code.Should().Be("FTL");
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadErrorAsync(duplicate)).Code.Should().Be("CAPABILITY_ALREADY_ASSIGNED");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);
        removedAgain.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var list = await _client.GetFromJsonAsync<List<CapabilityDto>>($"{BaseUrl}/{transporter.Id}/capabilities", TestHost.Json);
        list!.Should().ContainSingle(c => c.Id == capability.Id)
            .Which.Status.Should().Be(RecordStatus.Inactive);
    }

    [Fact]
    public async Task Capability_type_must_be_active_and_exist()
    {
        var transporter = await CreateTransporterAsync();

        var response = await _client.PostAsJsonAsync($"{BaseUrl}/{transporter.Id}/capabilities",
            new AddCapabilityRequest(987_654, new DateTime(2026, 1, 1)), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ReadErrorAsync(response)).Code.Should().Be("CAPABILITY_TYPE_INVALID");
    }

    [Fact]
    public async Task Blacklisted_transporter_cannot_gain_fleet_or_coverage()
    {
        var created = await CreateTransporterAsync();
        await _host.WithTransporterDbAsync(async db =>
        {
            (await db.Transporters.SingleAsync(t => t.Id == created.Id)).Status = TransporterStatus.Blacklisted;
            await db.SaveChangesAsync();
            return true;
        });

        var response = await _client.PostAsJsonAsync($"{BaseUrl}/{created.Id}/capabilities",
            new AddCapabilityRequest(1, new DateTime(2026, 1, 1)), TestHost.Json);

        (await ReadErrorAsync(response)).Code.Should().Be("TRANSPORTER_LOCKED");
    }

    // ---------- lookups & audit ----------

    [Fact]
    public async Task Lookups_return_only_active_configured_values()
    {
        var types = await _client.GetFromJsonAsync<List<LookupDto>>("/api/v1/transporter-management/lookups/transporter-types", TestHost.Json);
        var capabilities = await _client.GetFromJsonAsync<List<LookupDto>>("/api/v1/transporter-management/lookups/capability-types", TestHost.Json);

        types.Should().HaveCount(9).And.Contain(t => t.Code == "3PL");
        capabilities.Should().HaveCount(11).And.Contain(c => c.Code == "HAZARDOUS");
    }

    [Fact]
    public async Task Creating_and_updating_a_transporter_writes_audit_rows_in_the_same_transaction()
    {
        var created = await CreateTransporterAsync();
        await _client.PutAsJsonAsync($"{BaseUrl}/{created.Id}",
            new UpdateTransporterRequest("Renamed Logistics", null, 1, null, null, null, null, null, null, null, null, null, null, null, null),
            TestHost.Json);

        var actions = await _host.WithTransporterDbAsync(db => db.AuditLogs
            .Where(a => a.EntityType == "Transporter" && a.EntityId == created.Id.ToString())
            .OrderBy(a => a.Id)
            .Select(a => a.Action)
            .ToListAsync());

        actions.Should().Equal("Created", "Updated");
    }
}
