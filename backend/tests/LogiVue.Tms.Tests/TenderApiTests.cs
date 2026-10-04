using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Milestone 5: tender creation and distribution, transporter responses, the vendor portal and its isolation.</summary>
public class TenderApiTests : IClassFixture<TestHost>
{
    private const string AllRoles = "Transport Admin,Transport Manager,Transport Executive,Compliance User,Finance User,Operations User";

    private readonly TestHost _host;
    private readonly HttpClient _admin;

    public TenderApiTests(TestHost host)
    {
        _host = host;
        _host.EnsureTransporterSchemaAsync().GetAwaiter().GetResult();
        _admin = Internal("admin", AllRoles);
    }

    // ---------- clients & fixtures ----------

    private HttpClient Internal(string user, string roles)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, user);
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, roles);
        return client;
    }

    private HttpClient Vendor(long transporterId)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, $"vendor-{transporterId}");
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.TransporterHeader, transporterId.ToString());
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, "Transporter Admin,Transporter Operations User,Transporter Viewer");
        return client;
    }

    private static DateTime Pickup => DateTime.UtcNow.AddDays(5).Date.AddHours(8);

    private CreateTenderRequest NewTender(FixtureLane lane, TenderType type, IReadOnlyList<long> transporters, decimal? rate = 42000m, decimal weight = 12000m) =>
        new(type, $"LD-{Guid.NewGuid():N}"[..12], lane.Origin, lane.Destination, "FTL", lane.VehicleType, weight, null,
            Pickup, Pickup.AddDays(2), DateTime.UtcNow.AddHours(4), rate, "INR", transporters, "Handle with care");

    private async Task<HttpResponseMessage> CreateAsync(CreateTenderRequest request) =>
        await _admin.PostAsJsonAsync("/api/v1/tenders", request, TestHost.Json);

    private async Task<TenderInvitationDto> CreateDirectAsync(FixtureLane lane, long transporterId, decimal? rate = 42000m)
    {
        var response = await CreateAsync(NewTender(lane, TenderType.Direct, [transporterId], rate));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<List<TenderInvitationDto>>(TestHost.Json))!.Single();
    }

    private async Task<TenderDetailDto> SendAsync(long invitationId)
    {
        var response = await _admin.PostAsync($"/api/v1/tenders/{invitationId}/send", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TenderDetailDto>(TestHost.Json))!;
    }

    private async Task<ApiError> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ApiError>(TestHost.Json))!;

    private async Task SetDeadlinePastAsync(long invitationId) =>
        await _host.WithTransporterDbAsync(async db =>
        {
            var tender = await db.Tenders.SingleAsync(t => t.Id == invitationId);
            tender.ResponseDeadline = DateTime.UtcNow.AddMinutes(-5);
            await db.SaveChangesAsync();
            return true;
        });

    // ---------- creation & distribution ----------

    [Fact]
    public async Task Direct_tender_is_created_as_draft_and_sent_on_demand()
    {
        var lane = FixtureLane.Unique();
        var transporter = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Direct Co", 42000m));

        var invitation = await CreateDirectAsync(lane, transporter);
        var sent = await SendAsync(invitation.Id);

        invitation.Status.Should().Be(TenderStatus.Draft);
        sent.Invitation.Status.Should().Be(TenderStatus.Sent);
        sent.Events.Select(e => e.EventType).Should().Contain(new[] { "Created", "Sent" });
    }

    [Fact]
    public async Task Ineligible_transporter_cannot_be_tendered()
    {
        var lane = FixtureLane.Unique();
        var suspended = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Suspended Tender Co", 42000m, Status: TransporterStatus.Suspended));

        var response = await CreateAsync(NewTender(lane, TenderType.Direct, [suspended]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await ErrorOf(response);
        error.Code.Should().Be("TENDER_TRANSPORTER_INELIGIBLE");
        error.Details.Should().Contain(d => d.Reason.Contains("suspended"));
    }

    [Fact]
    public async Task Direct_tender_must_have_exactly_one_transporter()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Count A", 42000m));
        var b = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Count B", 41000m));

        var response = await CreateAsync(NewTender(lane, TenderType.Direct, [a, b]));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(response)).Code.Should().Be("TENDER_TRANSPORTER_COUNT");
    }

    [Fact]
    public async Task Sequential_tender_contacts_the_next_transporter_only_after_a_rejection()
    {
        var lane = FixtureLane.Unique();
        var first = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Seq First", 42000m));
        var second = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Seq Second", 43000m));
        var created = await CreateAsync(NewTender(lane, TenderType.Sequential, [first, second]));
        var invitations = (await created.Content.ReadFromJsonAsync<List<TenderInvitationDto>>(TestHost.Json))!;
        var firstInvitation = invitations.Single(i => i.TransporterId == first);
        var secondInvitation = invitations.Single(i => i.TransporterId == second);

        await SendAsync(firstInvitation.Id);
        var beforeRejection = await _admin.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{secondInvitation.Id}", TestHost.Json);
        var reject = await Vendor(first).PostAsJsonAsync($"/api/v1/vendor/tenders/{firstInvitation.Id}/reject",
            new RejectTenderRequest("RATE_ISSUE", null), TestHost.Json);
        var afterRejection = await _admin.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{secondInvitation.Id}", TestHost.Json);

        beforeRejection!.Invitation.Status.Should().Be(TenderStatus.Draft);
        reject.StatusCode.Should().Be(HttpStatusCode.OK);
        afterRejection!.Invitation.Status.Should().Be(TenderStatus.Sent);
    }

    [Fact]
    public async Task Broadcast_award_cancels_the_other_invitations()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Broadcast A", 42000m));
        var b = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Broadcast B", 43000m));
        var created = await CreateAsync(NewTender(lane, TenderType.Broadcast, [a, b]));
        var invitations = (await created.Content.ReadFromJsonAsync<List<TenderInvitationDto>>(TestHost.Json))!;
        var winner = invitations.Single(i => i.TransporterId == a);
        var other = invitations.Single(i => i.TransporterId == b);
        await SendAsync(winner.Id);
        await _admin.PostAsync($"/api/v1/tenders/{other.Id}/send", null);

        await Vendor(a).PostAsJsonAsync($"/api/v1/vendor/tenders/{winner.Id}/accept", new AcceptTenderRequest(null), TestHost.Json);
        var awarded = await _admin.PostAsJsonAsync($"/api/v1/tenders/{winner.Id}/award", new CommentsRequest("Best lane fit"), TestHost.Json);
        var otherAfter = await _admin.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{other.Id}", TestHost.Json);

        awarded.StatusCode.Should().Be(HttpStatusCode.OK);
        (await awarded.Content.ReadFromJsonAsync<TenderDetailDto>(TestHost.Json))!.Invitation.Status.Should().Be(TenderStatus.Awarded);
        otherAfter!.Invitation.Status.Should().Be(TenderStatus.Cancelled);
    }

    [Fact]
    public async Task Awarded_tender_cannot_be_cancelled()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Award Cancel Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);
        await Vendor(a).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/accept", new AcceptTenderRequest(null), TestHost.Json);
        await _admin.PostAsJsonAsync($"/api/v1/tenders/{invitation.Id}/award", new CommentsRequest(null), TestHost.Json);

        var response = await _admin.PostAsJsonAsync($"/api/v1/tenders/{invitation.Id}/cancel", new CommentsRequest("Changed mind"), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(response)).Code.Should().Be("TENDER_AWARDED");
    }

    // ---------- responses ----------

    [Fact]
    public async Task Vendor_opens_accepts_and_the_load_appears_in_their_loads()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Accept Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);
        var vendor = Vendor(a);

        var opened = await vendor.GetFromJsonAsync<TenderDetailDto>($"/api/v1/vendor/tenders/{invitation.Id}", TestHost.Json);
        var accepted = await vendor.PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/accept", new AcceptTenderRequest("We can do this"), TestHost.Json);
        var loads = await vendor.GetFromJsonAsync<List<VendorLoadDto>>("/api/v1/vendor/loads", TestHost.Json);

        opened!.Invitation.Status.Should().Be(TenderStatus.Viewed);
        accepted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await accepted.Content.ReadFromJsonAsync<TenderDetailDto>(TestHost.Json))!.Invitation.Status.Should().Be(TenderStatus.Accepted);
        loads!.Should().ContainSingle(l => l.InvitationId == invitation.Id && l.LoadStatus == "Assigned");
    }

    [Fact]
    public async Task Reject_requires_a_known_reason_and_a_comment_for_other()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Reject Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);
        var vendor = Vendor(a);

        var unknown = await vendor.PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/reject", new RejectTenderRequest("BORED", null), TestHost.Json);
        var otherNoComment = await vendor.PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/reject", new RejectTenderRequest("OTHER", " "), TestHost.Json);
        var missingCode = await vendor.PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/reject", new RejectTenderRequest("", null), TestHost.Json);

        (await ErrorOf(unknown)).Code.Should().Be("REJECTION_REASON_INVALID");
        (await ErrorOf(otherNoComment)).Code.Should().Be("REJECTION_COMMENT_REQUIRED");
        missingCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Rejection_with_a_valid_reason_closes_the_invitation_and_raises_an_alert()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Rejected Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);

        var response = await Vendor(a).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/reject",
            new RejectTenderRequest("VEHICLE_UNAVAILABLE", null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TenderDetailDto>(TestHost.Json))!.Invitation.Status.Should().Be(TenderStatus.Rejected);
        var alerts = await _host.WithTransporterDbAsync(db => db.Alerts.AnyAsync(x => x.AlertType == "TENDER_REJECTED" && x.EntityId == invitation.Id.ToString()));
        alerts.Should().BeTrue();
    }

    [Fact]
    public async Task Counter_offer_is_refused_while_the_feature_is_disabled()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Counter Off Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);

        var response = await Vendor(a).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/counter-offer",
            new CounterOfferRequest(45000m, "Fuel is up"), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorOf(response)).Code.Should().Be("COUNTER_OFFER_DISABLED");
    }

    [Fact]
    public async Task Counter_offer_is_recorded_when_the_feature_is_enabled()
    {
        await SetCounterOfferAsync(true);
        try
        {
            var lane = FixtureLane.Unique();
            var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Counter On Co", 42000m));
            var invitation = await CreateDirectAsync(lane, a);
            await SendAsync(invitation.Id);

            var response = await Vendor(a).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/counter-offer",
                new CounterOfferRequest(45000m, "Fuel is up"), TestHost.Json);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var detail = (await response.Content.ReadFromJsonAsync<TenderDetailDto>(TestHost.Json))!;
            detail.Responses.Should().Contain(r => r.Response == TenderResponseType.CounterOffered && r.QuotedRate == 45000m);
        }
        finally
        {
            await SetCounterOfferAsync(false);
        }
    }

    [Fact]
    public async Task Accepting_at_a_different_rate_is_refused()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Rate Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);

        var response = await Vendor(a).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/accept",
            new AcceptTenderRequest(null, QuotedRate: 39000m), TestHost.Json);

        (await ErrorOf(response)).Code.Should().Be("RATE_MISMATCH");
    }

    [Fact]
    public async Task Unanswered_invitation_past_its_deadline_expires_on_response_and_refuses_the_acceptance()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Late Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);
        await SetDeadlinePastAsync(invitation.Id);

        var response = await Vendor(a).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/accept", new AcceptTenderRequest(null), TestHost.Json);
        var detail = await _admin.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{invitation.Id}", TestHost.Json);

        (await ErrorOf(response)).Code.Should().Be("TENDER_EXPIRED");
        detail!.Invitation.Status.Should().Be(TenderStatus.Expired);
    }

    [Fact]
    public async Task Scheduled_expiry_marks_overdue_invitations_expired()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Sweep Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);
        await SetDeadlinePastAsync(invitation.Id);

        await _host.InScopeAsync(sp => sp.GetRequiredService<ITenderLifecycleService>().ExpireDueAsync());

        var detail = await _admin.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{invitation.Id}", TestHost.Json);
        detail!.Invitation.Status.Should().Be(TenderStatus.Expired);
    }

    // ---------- vehicle & driver ----------

    [Fact]
    public async Task Vendor_confirms_a_fleet_vehicle_and_driver_for_an_accepted_load()
    {
        var lane = FixtureLane.Unique();
        var seeded = await TransporterFixtures.SeedDetailedAsync(_host, lane, new FixtureProfile("Vehicle Co", 42000m));
        var invitation = await CreateDirectAsync(lane, seeded.Id);
        await SendAsync(invitation.Id);
        var vendor = Vendor(seeded.Id);
        await vendor.PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/accept", new AcceptTenderRequest(null), TestHost.Json);

        var response = await vendor.PostAsJsonAsync($"/api/v1/vendor/loads/{invitation.Id}/vehicle",
            new VehicleAssignmentRequest(seeded.VehicleRegistration.ToLowerInvariant(), "Amara Okafor", "+91 98200 55555",
                Pickup.AddHours(-2), Pickup.AddDays(1)), TestHost.Json);
        var loads = await vendor.GetFromJsonAsync<List<VendorLoadDto>>("/api/v1/vendor/loads", TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        loads!.Single(l => l.InvitationId == invitation.Id).LoadStatus.Should().Be("Vehicle Confirmed");
        loads.Single(l => l.InvitationId == invitation.Id).DriverName.Should().Be("Amara Okafor");
    }

    [Fact]
    public async Task Vehicle_outside_the_callers_fleet_is_refused()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Fleet Check Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);
        var vendor = Vendor(a);
        await vendor.PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/accept", new AcceptTenderRequest(null), TestHost.Json);

        var response = await vendor.PostAsJsonAsync($"/api/v1/vendor/loads/{invitation.Id}/vehicle",
            new VehicleAssignmentRequest("ZZ99 NOTREAL", "Driver", "+91 98200 55555", null, null), TestHost.Json);

        (await ErrorOf(response)).Code.Should().Be("VEHICLE_NOT_IN_FLEET");
    }

    [Fact]
    public async Task Driver_mobile_is_validated_on_the_server()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Mobile Check Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);

        var response = await Vendor(a).PostAsJsonAsync($"/api/v1/vendor/loads/{invitation.Id}/vehicle",
            new VehicleAssignmentRequest("MH12AB1234", "Driver", "not a phone", null, null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorOf(response)).Details.Should().Contain(d => d.Field == nameof(VehicleAssignmentRequest.DriverMobile));
    }

    // ---------- vendor isolation ----------

    [Fact]
    public async Task Vendor_cannot_see_or_act_on_another_transporters_tender()
    {
        var lane = FixtureLane.Unique();
        var owner = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Owner Co", 42000m));
        var intruder = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Intruder Co", 41000m));
        var invitation = await CreateDirectAsync(lane, owner);
        await SendAsync(invitation.Id);
        var attacker = Vendor(intruder);

        var read = await attacker.GetAsync($"/api/v1/vendor/tenders/{invitation.Id}");
        var accept = await attacker.PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/accept", new AcceptTenderRequest(null), TestHost.Json);
        var reject = await attacker.PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation.Id}/reject", new RejectTenderRequest("OTHER", "x"), TestHost.Json);
        var list = await attacker.GetFromJsonAsync<PagedResult<TenderInvitationDto>>("/api/v1/vendor/tenders", TestHost.Json);

        read.StatusCode.Should().Be(HttpStatusCode.NotFound);
        accept.StatusCode.Should().Be(HttpStatusCode.NotFound);
        reject.StatusCode.Should().Be(HttpStatusCode.NotFound);
        list!.Items.Should().NotContain(i => i.Id == invitation.Id);

        var stillSent = await _admin.GetFromJsonAsync<TenderDetailDto>($"/api/v1/tenders/{invitation.Id}", TestHost.Json);
        stillSent!.Invitation.Status.Should().Be(TenderStatus.Sent, "the intruder's attempts must not change the tender");
    }

    [Fact]
    public async Task Vendor_cannot_read_another_transporters_profile_through_the_internal_api()
    {
        var lane = FixtureLane.Unique();
        var other = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Internal Block Co", 42000m));
        var vendor = Vendor(await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Vendor Viewer Co", 41000m)));

        var transporter = await vendor.GetAsync($"/api/v1/transporters/{other}");
        var tenders = await vendor.GetAsync("/api/v1/tenders");
        var fleet = await vendor.GetAsync($"/api/v1/transporters/{other}/vehicles");

        transporter.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        tenders.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        fleet.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Internal_users_cannot_use_the_vendor_portal()
    {
        var response = await _admin.GetAsync("/api/v1/vendor/dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorOf(response)).Code.Should().Be("INTERNAL_ACCESS_ONLY");
    }

    [Fact]
    public async Task Vendor_dashboard_reflects_new_and_pending_tenders()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Dashboard Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);

        var dashboard = await Vendor(a).GetFromJsonAsync<VendorDashboardDto>("/api/v1/vendor/dashboard", TestHost.Json);

        dashboard!.NewTenders.Should().Be(1);
        dashboard.PendingAcceptance.Should().Be(1);
        dashboard.PendingPod.Should().Be(0, "no delivery has happened, so no POD is pending");
    }

    [Fact]
    public async Task Draft_invitations_are_invisible_to_the_vendor()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Draft Hidden Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);

        var response = await Vendor(a).GetAsync($"/api/v1/vendor/tenders/{invitation.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Open_tender_can_be_cancelled_and_its_invitations_end_cancelled()
    {
        var lane = FixtureLane.Unique();
        var a = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Cancel Co", 42000m));
        var invitation = await CreateDirectAsync(lane, a);
        await SendAsync(invitation.Id);

        var response = await _admin.PostAsJsonAsync($"/api/v1/tenders/{invitation.Id}/cancel", new CommentsRequest("Load withdrawn"), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TenderDetailDto>(TestHost.Json))!.Invitation.Status.Should().Be(TenderStatus.Cancelled);
    }

    private async Task SetCounterOfferAsync(bool enabled) =>
        await _host.WithTransporterDbAsync(async db =>
        {
            var setting = await db.ConfigurationSettings.SingleAsync(s => s.Key == SettingKeys.TenderAllowCounterOffer);
            setting.ValueJson = enabled ? "true" : "false";
            setting.Version++;
            await db.SaveChangesAsync();
            return true;
        });
}
