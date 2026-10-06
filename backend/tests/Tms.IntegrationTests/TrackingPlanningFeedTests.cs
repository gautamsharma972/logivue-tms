using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Shipments.Application;
using Tms.Modules.Tracking.Application;
using Tms.Modules.Tracking.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TrackingPlanningFeedTests(TmsApiFactory factory)
{
    [Fact]
    public async Task A_shipment_that_is_dispatched_becomes_a_trip_ready_to_track_with_its_stops_and_vehicle_from_planning()
    {
        using var s = await ShipmentScenario.CreateAsync(factory);
        var first = await s.OrderAsync(3_000m, "Surat");
        var second = await s.OrderAsync(4_000m, "Vadodara");
        var tendered = await s.TenderedAsync(first, second);
        (await s.Vendor.PostJsonAsync($"/api/v1/shipments/{tendered.Summary.Id}/accept", new AcceptRequest(s.Vehicle.Id, s.Driver.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var dispatched = await s.Admin.PostAsync($"/api/v1/shipments/{tendered.Summary.Id}/dispatch", null);
        dispatched.StatusCode.ShouldBe(HttpStatusCode.OK, await dispatched.Content.ReadAsStringAsync());

        var found = await (await s.Admin.GetAsync($"/api/v1/tracking/shipments?search={tendered.Summary.Number}")).ReadAsync<PagedResult<TrackedShipmentSummaryDto>>();
        var trip = found.Items.ShouldHaveSingleItem();
        trip.TripReference.ShouldBe(tendered.Summary.Number); // the trip is the shipment: one number for both
        trip.Execution.ShouldBe(ExecutionStatus.Planned);
        trip.Tracking.ShouldBe(TrackingHealth.NotStarted);
        trip.TransporterId.ShouldBe(s.Transporter.Id);
        trip.VehicleReference.ShouldBe(s.Vehicle.RegistrationNumber);

        var detail = await (await s.Admin.GetAsync($"/api/v1/tracking/shipments/{trip.Id}")).ReadAsync<TrackedShipmentDto>();
        detail.Stops.Count(x => x.Kind == StopKind.Drop).ShouldBe(2);
        detail.Stops.Count(x => x.Kind == StopKind.Pickup).ShouldBeGreaterThanOrEqualTo(1);
        detail.Stops.Where(x => x.Kind == StopKind.Drop).Select(x => x.City).ShouldBe(["Surat", "Vadodara"], ignoreOrder: true);
        detail.Stops.ShouldAllBe(x => x.PlannedArrival != null);
        detail.RouteSource.ShouldBe("Estimate"); // no road server is configured here, so it says so rather than drawing a road
        (await (await s.Admin.GetAsync($"/api/v1/tracking/shipments/{trip.Id}/timeline")).ReadAsync<List<TimelineEntryDto>>()).ShouldContain(e => e.Type == ShipmentEventTypes.Dispatched);

        // A carrier's user can only track trips once it has been given the tracking permission: shipping rights alone do not open the driver's tracking.
        (await s.Vendor.GetAsync("/api/v1/mobile/tracking/trips")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
