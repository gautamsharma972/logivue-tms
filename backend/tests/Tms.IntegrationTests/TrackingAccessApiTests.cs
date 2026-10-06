using System.Net;
using System.Text.Json;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Tracking.Application;
using Tms.Modules.Tracking.Application.Queries;
using Tms.Modules.Tracking.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TrackingAccessApiTests(TmsApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    [Fact]
    public async Task A_carrier_sees_and_runs_only_its_own_trips_and_another_carriers_are_not_found()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.1, 5));
        var id = await s.TrackedIdAsync();

        (await s.Driver.GetAsync($"/api/v1/tracking/shipments/{id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        foreach (var path in new[] { "", "/current-location", "/timeline", "/eta", "/route", "/locations", "/exceptions", "/health", "/analytics" })
        {
            (await s.Rival.GetAsync($"/api/v1/tracking/shipments/{id}{path}")).StatusCode.ShouldBe(HttpStatusCode.NotFound, path);
        }

        (await s.Rival.PostJsonAsync("/api/v1/mobile/tracking/start", new Tms.Modules.Tracking.Application.Mobile.StartTrackingRequest(s.TripReference, "RIVAL-PHONE"))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.SendRawAsync([s.Fix(0.2, 2)], s.Rival)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Rival.PostJsonAsync("/api/v1/mobile/tracking/stop", new Tms.Modules.Tracking.Application.Mobile.StopTrackingRequest(s.TripReference))).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var theirs = await (await s.Rival.GetAsync("/api/v1/tracking/shipments?pageSize=100")).ReadAsync<PagedResult<TrackedShipmentSummaryDto>>();
        theirs.Items.ShouldNotContain(x => x.TripReference == s.TripReference);
        var summary = await (await s.Rival.GetAsync("/api/v1/control-tower/summary")).ReadAsync<ControlTowerSummaryDto>();
        summary.Active.ShouldBe(0);
    }

    [Fact]
    public async Task A_driver_cannot_manage_alerts_geofences_links_settings_overrides_or_reports()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        var id = await s.TrackedIdAsync();
        var geofence = new SaveGeofenceRequest("DRV1", "x", GeofenceType.Custom, 19, 73, 200, null, null, null, null, null);

        (await s.Driver.PostJsonAsync("/api/v1/tracking/geofences", geofence)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Driver.PostJsonAsync("/api/v1/tracking/links", new CreateLinkRequest(id, null, null, 7))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Driver.PutJsonAsync("/api/v1/tracking/settings/tracking.interval", new { activeSeconds = 60 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Driver.PostJsonAsync($"/api/v1/tracking/shipments/{id}/eta/override", new OverrideEtaRequest(DateTimeOffset.UtcNow.AddHours(2), "x"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Driver.GetAsync("/api/v1/tracking/reports/shipments")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Driver.PostJsonAsync("/api/v1/tracking/eta/recalculate", new RecalculateEtaRequest(id, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Someone_with_no_tracking_permission_sees_nothing()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        var nobody = await factory.UserWithPermissionsAsync(s.Admin, "transporters.read");

        (await nobody.GetAsync("/api/v1/tracking/shipments")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync("/api/v1/control-tower/summary")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync("/api/v1/tracking/vehicles")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync("/api/v1/tracking/alerts")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync("/api/v1/mobile/tracking/trips")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_customer_link_shows_where_the_goods_are_and_nothing_internal_and_can_be_revoked()
    {
        using var s = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(120));
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.0, 100, speed: 0), s.Fix(0.0, 97, speed: 0), s.Fix(0.3, 50), s.Fix(0.5, 2));
        var id = await s.TrackedIdAsync();

        var created = await s.Admin.PostJsonAsync("/api/v1/tracking/links", new CreateLinkRequest(id, "CUST-1", "ABC Distributors", 7));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var link = await created.ReadAsync<CreatedLinkDto>();
        link.Token.Length.ShouldBeGreaterThanOrEqualTo(40);
        link.Path.ShouldBe($"/track/{link.Token}");
        link.Link.Status.ShouldBe(LinkStatus.Active);

        var anonymous = factory.CreateClient();
        var view = await anonymous.GetAsync($"/api/v1/public/tracking/{link.Token}");
        view.StatusCode.ShouldBe(HttpStatusCode.OK, await view.Content.ReadAsStringAsync());
        var text = await view.Content.ReadAsStringAsync();
        var customer = JsonSerializer.Deserialize<CustomerTrackingDto>(text, Json)!;
        customer.ShipmentReference.ShouldBe(s.TripReference);
        customer.StatusLabel.ShouldBe("In transit");
        customer.Steps.Select(x => x.Label).ShouldContain("In transit");
        customer.Steps.Single(x => x.Label == "Delivered").State.ShouldBe("pending");
        customer.EtaAt.ShouldNotBeNull();
        customer.Latitude.ShouldNotBeNull();
        customer.LocationLabel.ShouldNotBeNullOrEmpty();
        customer.Route.ShouldNotBeEmpty();

        // None of what is internal appears anywhere in what was sent.
        foreach (var internalWord in new[] { s.Transporter.LegalName, s.VehicleReference, "Ramesh", "9876543210", "exception", "TEX-", "alert", "confidence", "deviation", "Mumbai DC" })
        {
            text.ShouldNotContain(internalWord, Case.Insensitive);
        }

        (await s.Admin.GetAsync($"/api/v1/tracking/shipments/{id}/links")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var listed = await (await s.Admin.GetAsync($"/api/v1/tracking/shipments/{id}/links")).ReadAsync<List<CustomerLinkDto>>();
        listed.Single().ViewCount.ShouldBeGreaterThanOrEqualTo(1);
        text.ShouldNotContain(link.Link.Id.ToString("D")); // no internal identifiers either

        var revoked = await s.Admin.PostAsync($"/api/v1/tracking/links/{link.Link.Id}/revoke", null);
        revoked.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await anonymous.GetAsync($"/api/v1/public/tracking/{link.Token}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_made_up_or_malformed_link_is_not_found_and_a_link_opens_only_its_own_shipment()
    {
        using var a = await TrackingScenario.CreateAsync(factory);
        using var b = await TrackingScenario.CreateAsync(factory);
        await a.StartAsync();
        await b.StartAsync();
        var linkA = await (await a.Admin.PostJsonAsync("/api/v1/tracking/links", new CreateLinkRequest(await a.TrackedIdAsync(), null, null, null))).ReadAsync<CreatedLinkDto>();
        var anonymous = factory.CreateClient();

        (await anonymous.GetAsync($"/api/v1/public/tracking/{new string('a', 43)}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync("/api/v1/public/tracking/short")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await anonymous.GetAsync($"/api/v1/public/tracking/{linkA.Token[..^1]}x")).StatusCode.ShouldBe(HttpStatusCode.NotFound); // one character off
        (await anonymous.GetAsync("/api/v1/public/tracking/../../tracking/shipments")).StatusCode.ShouldBeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Unauthorized);

        var view = await (await anonymous.GetAsync($"/api/v1/public/tracking/{linkA.Token}")).ReadAsync<CustomerTrackingDto>();
        view.ShipmentReference.ShouldBe(a.TripReference);
        view.ShipmentReference.ShouldNotBe(b.TripReference);
        (await anonymous.GetAsync("/api/v1/tracking/shipments")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized); // the link is not a login
    }

    [Fact]
    public async Task Geofences_can_be_made_changed_and_removed_and_one_that_has_been_used_is_only_switched_off()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        var code = $"GF{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var (lat, lon) = TrackingScenario.Along(0.4);

        var made = await s.Admin.PostJsonAsync("/api/v1/tracking/geofences", new SaveGeofenceRequest(code.ToLowerInvariant(), "Toll plaza", GeofenceType.Toll, lat, lon, 300, null, null, null, null, null));
        made.StatusCode.ShouldBe(HttpStatusCode.Created, await made.Content.ReadAsStringAsync());
        var geofence = await made.ReadAsync<GeofenceDto>();
        geofence.Code.ShouldBe(code); // codes are kept in one case so they cannot be duplicated by capitalisation
        (await s.Admin.PostJsonAsync("/api/v1/tracking/geofences", new SaveGeofenceRequest(code, "Again", GeofenceType.Toll, lat, lon, 300, null, null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Admin.PostJsonAsync("/api/v1/tracking/geofences", new SaveGeofenceRequest("BAD1", "Too small", GeofenceType.Custom, lat, lon, 5, null, null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PostJsonAsync("/api/v1/tracking/geofences", new SaveGeofenceRequest("BAD2", "Bad polygon", GeofenceType.Custom, lat, lon, 100, [[19, 73], [19, 73.1]], null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        double[][] ring = [[lat - 0.002, lon - 0.002], [lat - 0.002, lon + 0.002], [lat + 0.002, lon + 0.002], [lat + 0.002, lon - 0.002]];
        var changed = await s.Admin.PutJsonAsync($"/api/v1/tracking/geofences/{geofence.Id}", new SaveGeofenceRequest(code, "Toll plaza (polygon)", GeofenceType.Toll, lat, lon, 300, ring, null, null, null, geofence.Version));
        changed.StatusCode.ShouldBe(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());
        (await changed.ReadAsync<GeofenceDto>()).Polygon!.Count.ShouldBe(4);
        (await s.Admin.PutJsonAsync($"/api/v1/tracking/geofences/{geofence.Id}", new SaveGeofenceRequest(code, "stale", GeofenceType.Toll, lat, lon, 300, null, null, null, null, geofence.Version))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // an old copy cannot overwrite

        // A trip passes through it, so it has been used...
        await s.StartAsync();
        await s.SendAsync(s.At(lat, lon, 30, 20), s.At(lat, lon, 26, 20));
        (await s.Admin.DeleteAsync($"/api/v1/tracking/geofences/{geofence.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        var all = await (await s.Admin.GetAsync("/api/v1/tracking/geofences")).ReadAsync<List<GeofenceDto>>();
        all.Single(g => g.Id == geofence.Id).Status.ShouldBe(GeofenceStatus.Inactive); // ...so it is switched off, not erased

        var unused = await (await s.Admin.PostJsonAsync("/api/v1/tracking/geofences", new SaveGeofenceRequest($"{code[..8]}ZZ", "Unused", GeofenceType.Custom, 12, 77, 300, null, null, null, null, null))).ReadAsync<GeofenceDto>();
        (await s.Admin.DeleteAsync($"/api/v1/tracking/geofences/{unused.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await (await s.Admin.GetAsync("/api/v1/tracking/geofences")).ReadAsync<List<GeofenceDto>>()).ShouldNotContain(g => g.Id == unused.Id);
    }

    [Fact]
    public async Task Settings_have_defaults_change_per_tenant_and_reject_nonsense()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        var listed = await (await s.Admin.GetAsync("/api/v1/tracking/settings")).ReadAsync<List<SettingDto>>();
        listed.Select(x => x.Key).ShouldBe(TrackingSettingDefaults.Keys, ignoreOrder: true);
        listed.ShouldAllBe(x => !x.IsCustomised || x.Key != "tracking.nonsense");

        (await s.Admin.PutJsonAsync("/api/v1/tracking/settings/tracking.health", new { staleAfterMinutes = 30, lostAfterMinutes = 20 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // lost must come after stale
        (await s.Admin.PutJsonAsync("/api/v1/tracking/settings/tracking.route", new { deviationKm = 5, highKm = 3, criticalKm = 10 })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PutJsonAsync("/api/v1/tracking/settings/tracking.nonsense", new { })).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await s.Driver.PutJsonAsync("/api/v1/tracking/settings/tracking.interval", new { activeSeconds = 60 })).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        try
        {
            var saved = await s.Admin.PutJsonAsync("/api/v1/tracking/settings/tracking.interval", new { activeSeconds = 60, stationarySeconds = 300, approachingSeconds = 30, approachingKm = 5, adaptive = true });
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            (await s.StartAsync()).IntervalSeconds.ShouldBe(60); // the phone is told, so the interval is never hard-coded in the app
        }
        finally
        {
            await s.Admin.PutJsonAsync("/api/v1/tracking/settings/tracking.interval", new { activeSeconds = 180, stationarySeconds = 600, approachingSeconds = 60, approachingKm = 10, adaptive = true });
        }
    }

    [Fact]
    public async Task Every_report_downloads_as_csv_or_excel_for_staff()
    {
        using var s = await TrackingScenario.CreateAsync(factory);
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.1, 12), s.Fix(0.14, 9), s.Fix(0.18, 6));
        var range = $"from={DateTime.UtcNow.AddDays(-2):yyyy-MM-dd}&to={DateTime.UtcNow.AddDays(1):yyyy-MM-dd}";

        foreach (var report in ReportsHandler.Reports)
        {
            var csv = await s.Admin.GetAsync($"/api/v1/tracking/reports/{report}?format=csv&{range}");
            csv.StatusCode.ShouldBe(HttpStatusCode.OK, report);
            csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
            var xlsx = await s.Admin.GetAsync($"/api/v1/tracking/reports/{report}?format=xlsx&{range}");
            xlsx.StatusCode.ShouldBe(HttpStatusCode.OK, report);
            xlsx.Content.Headers.ContentType!.MediaType!.ShouldContain("spreadsheetml");
        }

        var shipments = await (await s.Admin.GetAsync($"/api/v1/tracking/reports/shipments?{range}&vehicle={s.VehicleReference}")).Content.ReadAsStringAsync();
        shipments.ShouldContain(s.TripReference);
        shipments.ShouldContain("Planned arrival"); // the planned and the expected are separate columns
        (await s.Admin.GetAsync("/api/v1/tracking/reports/shipments?format=pdf")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync("/api/v1/tracking/reports/nonsense")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
