using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Tracking.Application;
using Tms.Modules.Tracking.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TrackingDemoSeedTests(TmsApiFactory factory)
{
    private sealed record Carrier(Guid Id, string Name);

    private sealed record SeedBody(List<Carrier> Carriers);

    [Fact]
    public async Task The_demo_day_shows_trips_in_every_condition_and_can_be_run_twice_without_doubling()
    {
        var admin = await factory.AdminAsync();
        var carrier = await ContractApiData.ActiveTransporterAsync(admin);
        var body = new SeedBody([new Carrier(carrier.Id, carrier.LegalName)]);

        // The seed adds named places (PUN-HUB and others). Other tests share the tenant and place their stops at the same spots, where a Hub's longer
        // dwell allowance would replace a Customer's, so the places this test adds are removed when it ends.
        async Task<HashSet<Guid>> GeofenceIds() => (await (await admin.GetAsync("/api/v1/tracking/geofences")).ReadAsync<List<GeofenceDto>>()).Select(g => g.Id).ToHashSet();
        var existing = await GeofenceIds();
        try
        {
            await RunAsync(admin, body);
        }
        finally
        {
            foreach (var id in (await GeofenceIds()).Except(existing))
            {
                await admin.DeleteAsync($"/api/v1/tracking/geofences/{id}");
            }
        }
    }

    private static async Task RunAsync(HttpClient admin, SeedBody body)
    {
        var first = await admin.PostJsonAsync("/api/v1/dev/tracking/seed-demo", body);
        first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync());

        async Task<TrackedShipmentSummaryDto> Trip(string reference) =>
            (await (await admin.GetAsync($"/api/v1/tracking/shipments?search={reference}")).ReadAsync<PagedResult<TrackedShipmentSummaryDto>>()).Items.ShouldHaveSingleItem();

        (await Trip("SH-10025")).Tracking.ShouldNotBe(TrackingHealth.NotStarted);
        (await Trip("SH-10031")).Tracking.ShouldBe(TrackingHealth.Lost);
        (await Trip("SH-10042")).Tracking.ShouldBe(TrackingHealth.Stale);
        (await Trip("SH-10054")).Tracking.ShouldBe(TrackingHealth.NotStarted);
        (await Trip("SH-10052")).Execution.ShouldBe(ExecutionStatus.Completed);

        var deviation = await Trip("SH-10025");
        var alerts = await (await admin.GetAsync("/api/v1/tracking/alerts?pageSize=100")).ReadAsync<PagedResult<AlertDto>>();
        alerts.Items.ShouldContain(a => a.TripReference == "SH-10025" && a.Type == AlertType.RouteDeviation);

        var before = (await (await admin.GetAsync("/api/v1/tracking/shipments?pageSize=100")).ReadAsync<PagedResult<TrackedShipmentSummaryDto>>()).TotalCount;
        (await admin.PostJsonAsync("/api/v1/dev/tracking/seed-demo", body)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await admin.GetAsync("/api/v1/tracking/shipments?pageSize=100")).ReadAsync<PagedResult<TrackedShipmentSummaryDto>>()).TotalCount.ShouldBe(before);
    }
}
