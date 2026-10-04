using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LogiVue.Tms.Shared.Common;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Monitoring;
using LogiVue.Tms.TransporterManagement.Application.Placement;
using LogiVue.Tms.TransporterManagement.Application.Scorecards;
using LogiVue.Tms.TransporterManagement.Application.Tendering;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Application.Workflow;
using LogiVue.Tms.TransporterManagement.Domain.Alerts;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;
using LogiVue.Tms.TransporterManagement.Domain.Placement;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Tendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>Follow-ups from the Milestone 2 to 6 reviews: service-type catalogue, counter-offer decisions, overdue
/// alerts, scorecards with the planning read model, and paged lists.</summary>
public class FollowUpTests : IClassFixture<TestHost>
{
    private const string AllRoles = "Transport Admin,Transport Manager,Transport Executive,Compliance User,Finance User,Operations User";
    private const string VendorRoles = "Transporter Admin,Transporter Operations User,Transporter Viewer";

    private readonly TestHost _host;
    private readonly HttpClient _admin;

    public FollowUpTests(TestHost host)
    {
        _host = host;
        _host.EnsureTransporterSchemaAsync().GetAwaiter().GetResult();
        _admin = Internal();
    }

    private HttpClient Internal()
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, "ops");
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, AllRoles);
        return client;
    }

    private HttpClient Vendor(long transporterId)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, $"vendor-{transporterId}");
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.TransporterHeader, transporterId.ToString());
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, VendorRoles);
        return client;
    }

    private static DateTime Pickup => DateTime.UtcNow.AddDays(5).Date.AddHours(8);

    private async Task<long> DirectTenderAsync(FixtureLane lane, long transporterId, string serviceType = "FTL", decimal rate = 42000m)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/tenders", new CreateTenderRequest(TenderType.Direct,
            $"LD-{Guid.NewGuid():N}"[..12], lane.Origin, lane.Destination, serviceType, lane.VehicleType, 12000m, null,
            Pickup, Pickup.AddDays(2), DateTime.UtcNow.AddHours(4), rate, "INR", [transporterId], null), TestHost.Json);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<List<TenderInvitationDto>>(TestHost.Json))!.Single().Id;
    }

    private async Task SetSettingAsync(string key, string json) =>
        await _host.WithTransporterDbAsync(async db =>
        {
            var setting = await db.ConfigurationSettings.SingleAsync(s => s.Key == key);
            setting.ValueJson = json;
            setting.Version++;
            await db.SaveChangesAsync();
            return true;
        });

    // ---------- service-type catalogue ----------

    [Fact]
    public async Task Tenders_only_use_active_service_types_from_the_catalogue()
    {
        var lane = FixtureLane.Unique();
        var seeded = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Catalogue Co", 42000m));

        var response = await _admin.PostAsJsonAsync("/api/v1/tenders", new CreateTenderRequest(TenderType.Direct,
            $"LD-{Guid.NewGuid():N}"[..12], lane.Origin, lane.Destination, "BOGUS", lane.VehicleType, 12000m, null,
            Pickup, Pickup.AddDays(2), DateTime.UtcNow.AddHours(4), 42000m, "INR", [seeded], null), TestHost.Json);
        var lookups = await _admin.GetFromJsonAsync<List<LookupDto>>("/api/v1/transporter-management/lookups/service-types", TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var error = await response.Content.ReadFromJsonAsync<ApiError>(TestHost.Json);
        error!.Code.Should().Be("SERVICE_TYPE_INVALID");
        lookups!.Should().Contain(l => l.Code == "FTL");
    }

    // ---------- counter-offer decisions ----------

    [Fact]
    public async Task Accepting_a_counter_offer_makes_it_the_offered_rate()
    {
        await SetSettingAsync(SettingKeys.TenderAllowCounterOffer, "true");
        var lane = FixtureLane.Unique();
        var seeded = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Counter Accept Co", 42000m));
        var invitation = await DirectTenderAsync(lane, seeded);
        await _admin.PostAsync($"/api/v1/tenders/{invitation}/send", null);
        await Vendor(seeded).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation}/counter-offer", new CounterOfferRequest(40000m, "Fuel is up"), TestHost.Json);

        var response = await _admin.PostAsJsonAsync($"/api/v1/tenders/{invitation}/counter-offer/accept", new CommentsRequest("Agreed"), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await _host.WithTransporterDbAsync(db => db.Tenders.AsNoTracking().SingleAsync(t => t.Id == invitation));
        row.Status.Should().Be(TenderStatus.Accepted);
        row.OfferedRate.Should().Be(40000m);
    }

    [Fact]
    public async Task Declining_a_counter_offer_closes_the_invitation_for_a_rate_issue()
    {
        await SetSettingAsync(SettingKeys.TenderAllowCounterOffer, "true");
        var lane = FixtureLane.Unique();
        var seeded = await TransporterFixtures.SeedAsync(_host, lane, new FixtureProfile("Counter Decline Co", 42000m));
        var invitation = await DirectTenderAsync(lane, seeded);
        await _admin.PostAsync($"/api/v1/tenders/{invitation}/send", null);
        await Vendor(seeded).PostAsJsonAsync($"/api/v1/vendor/tenders/{invitation}/counter-offer", new CounterOfferRequest(30000m, null), TestHost.Json);

        var response = await _admin.PostAsJsonAsync($"/api/v1/tenders/{invitation}/counter-offer/decline", new CommentsRequest(null), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = await _host.WithTransporterDbAsync(db => db.Tenders.AsNoTracking().SingleAsync(t => t.Id == invitation));
        row.Status.Should().Be(TenderStatus.Rejected);
        var rejection = await _host.WithTransporterDbAsync(db => db.TenderResponses.AsNoTracking()
            .SingleAsync(r => r.TenderId == invitation && r.Response == TenderResponseType.Rejected));
        rejection.Reason.Should().Be("RATE_ISSUE");
    }

    // ---------- overdue alerts ----------

    [Fact]
    public async Task Overdue_placement_raises_an_alert_that_resolves_once_the_vehicle_is_placed()
    {
        var seeded = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Overdue Co", 42000m));
        var placementId = await _host.WithTransporterDbAsync(async db =>
        {
            var placement = new VehiclePlacementRequest
            {
                LoadReference = $"LD-{Guid.NewGuid():N}"[..12], TransporterId = seeded, RequestedAt = DateTime.UtcNow.AddDays(-2),
                RequiredPlacementAt = DateTime.UtcNow.AddHours(-3), Status = PlacementStatus.Requested
            };
            db.VehiclePlacementRequests.Add(placement);
            await db.SaveChangesAsync();
            return placement.Id;
        });

        await _host.InScopeAsync(sp => sp.GetRequiredService<IOperationalMonitor>().RaiseOverdueAlertsAsync());
        var raised = await AlertFor(placementId, "PLACEMENT_OVERDUE");

        await _host.WithTransporterDbAsync(async db =>
        {
            var placement = await db.VehiclePlacementRequests.SingleAsync(p => p.Id == placementId);
            placement.Status = PlacementStatus.Placed;
            placement.PlacedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return true;
        });
        await _host.InScopeAsync(sp => sp.GetRequiredService<IOperationalMonitor>().RaiseOverdueAlertsAsync());
        var resolved = await AlertFor(placementId, "PLACEMENT_OVERDUE");

        raised.Status.Should().Be(AlertStatus.Open);
        resolved.Status.Should().Be(AlertStatus.Resolved);
    }

    private async Task<TransporterAlert> AlertFor(long entityId, string type) =>
        await _host.WithTransporterDbAsync(db => db.Alerts.AsNoTracking()
            .SingleAsync(a => a.AlertType == type && a.EntityType == "VehiclePlacement" && a.EntityId == entityId.ToString()));

    // ---------- scorecards and the planning read model ----------

    [Fact]
    public async Task Scorecard_scores_the_period_and_feeds_the_planning_read_model()
    {
        var seeded = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Scorecard Co", 42000m));

        var response = await _admin.PostAsJsonAsync($"/api/v1/transporters/{seeded}/scorecards",
            new GenerateScorecardRequest(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)), TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var scorecard = await response.Content.ReadFromJsonAsync<ScorecardDto>(TestHost.Json);
        scorecard!.OverallScore.Should().NotBeNull();
        scorecard.Kpis.Single(k => k.Kpi == KpiType.OnTimeDelivery).KpiValue.Should().Be(96.4m);
        scorecard.Kpis.Single(k => k.Kpi == KpiType.ClaimsRate).WeightedScore.Should().NotBeNull("claims are scored in the right direction");
        var feedback = await _host.WithTransporterDbAsync(db => db.PlanningFeedback.AsNoTracking()
            .SingleAsync(f => f.TransporterId == seeded && f.LaneReference == null));
        feedback.OverallScore.Should().Be(scorecard.OverallScore);
    }

    // ---------- paging ----------

    [Fact]
    public async Task Placement_list_is_paged_with_a_total()
    {
        var page = await _admin.GetFromJsonAsync<PagedResult<PlacementDto>>("/api/v1/vehicle-placement?page=1&pageSize=1", TestHost.Json);

        page!.PageSize.Should().Be(1);
        page.Items.Count.Should().BeLessThanOrEqualTo(1);
        page.TotalCount.Should().BeGreaterThanOrEqualTo(page.Items.Count);
    }
}
