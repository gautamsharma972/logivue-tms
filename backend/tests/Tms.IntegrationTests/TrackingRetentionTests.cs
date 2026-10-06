using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Tracking.Application;
using Tms.Modules.Tracking.Application.Engine;
using Tms.Modules.Tracking.Infrastructure.Persistence;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TrackingRetentionTests(TmsApiFactory factory)
{
    private async Task<int> RawCountAsync(Guid shipmentId)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<TrackingDbContext>().Locations.IgnoreQueryFilters().CountAsync(l => l.ShipmentId == shipmentId);
    }

    [Fact]
    public async Task A_trip_can_be_replayed_while_it_runs_and_from_its_saved_path_after_the_raw_points_are_purged()
    {
        using var s = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(200));
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.05, 190), s.Fix(0.2, 150), s.Fix(0.4, 110), s.Fix(0.6, 70), s.Fix(0.8, 30), s.Fix(0.97, 5));
        await s.StopAsync(completed: true);
        var id = await s.TrackedIdAsync();

        var live = await s.GetAsync<ReplayDto>("replay");
        live.Source.ShouldBe("Raw");
        live.Points.Count.ShouldBe(6);
        live.Points.Select(p => p[2]).ShouldBe(live.Points.Select(p => p[2]).OrderBy(x => x)); // in time order

        // the trip finished long ago: its raw points are now past the retention period
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
            await db.Shipments.IgnoreQueryFilters().Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow.AddDays(-120)));
            var result = await scope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(default);
            result.LocationsPurged.ShouldBeGreaterThanOrEqualTo(6);
        }

        (await RawCountAsync(s.ShipmentId)).ShouldBe(0);
        var kept = await s.GetAsync<ReplayDto>("replay");
        kept.Source.ShouldBe("Summary");
        kept.Points.Count.ShouldBeGreaterThanOrEqualTo(2);
        kept.Points[0][0].ShouldBe(TrackingScenario.Along(0.05).Lat, 0.001); // starts where it started
        kept.Points[^1][0].ShouldBe(TrackingScenario.Along(0.97).Lat, 0.001);
        (await s.ShipmentAsync()).Summary.Tracking.ShouldBe(Tms.Modules.Tracking.Domain.TrackingHealth.Completed); // the trip itself is untouched
    }

    [Fact]
    public async Task A_trip_still_on_the_road_keeps_every_point_however_old_they_are()
    {
        using var s = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(200));
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.1, 60), s.Fix(0.2, 30), s.Fix(0.3, 5));

        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<RetentionService>().RunAsync(default);
        }

        (await RawCountAsync(s.ShipmentId)).ShouldBe(3);
    }

    [Fact]
    public async Task A_tenants_own_retention_period_decides_what_is_old()
    {
        using var s = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(200));
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.1, 60), s.Fix(0.3, 5));
        await s.StopAsync(completed: true);
        var id = await s.TrackedIdAsync();

        // completed 20 days ago: kept under the default 90 days, purged once this tenant asks for 7
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TrackingDbContext>();
        await db.Shipments.IgnoreQueryFilters().Where(x => x.Id == id).ExecuteUpdateAsync(u => u.SetProperty(x => x.CompletedAt, DateTimeOffset.UtcNow.AddDays(-20)));
        var retention = scope.ServiceProvider.GetRequiredService<RetentionService>();
        await retention.RunAsync(default);
        (await RawCountAsync(s.ShipmentId)).ShouldBe(2);

        var saved = await s.Admin.PutJsonAsync("/api/v1/tracking/settings/tracking.retention", new { rawLocationDays = 7, aggregatedRouteDays = 365 });
        saved.IsSuccessStatusCode.ShouldBeTrue(await saved.Content.ReadAsStringAsync());
        await retention.RunAsync(default);
        (await RawCountAsync(s.ShipmentId)).ShouldBe(0);
        (await s.GetAsync<ReplayDto>("replay")).Source.ShouldBe("Summary");
    }
}

[Collection(ApiCollection.Name)]
public class TrackingComplianceApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task Compliance_counts_a_lost_gap_for_the_transporter_and_stays_with_staff()
    {
        using var s = await TrackingScenario.CreateAsync(factory, TimeSpan.FromMinutes(200));
        await s.StartAsync();
        await s.SendAsync(s.Fix(0.10, 90));
        (await s.ShipmentAsync()).Summary.Tracking.ShouldBe(Tms.Modules.Tracking.Domain.TrackingHealth.Lost);
        await s.SendAsync(s.Fix(0.30, 1));
        await s.StopAsync(completed: true);

        var report = await (await s.Admin.GetAsync($"/api/v1/tracking/compliance?transporterId={s.Transporter.Id}")).ReadAsync<Tms.Modules.Tracking.Application.Queries.ComplianceDto>();

        var row = report.Rows.ShouldHaveSingleItem();
        row.Trips.ShouldBe(1);
        row.Gaps.ShouldBeGreaterThanOrEqualTo(1);
        row.LostTrips.ShouldBe(1);
        row.StoppedProperly.ShouldBe(100);
        report.Note.ShouldContain("do not change a transporter's score");

        (await s.Driver.GetAsync("/api/v1/tracking/compliance")).StatusCode.ShouldBe(System.Net.HttpStatusCode.Forbidden);
        (await s.Admin.GetAsync("/api/v1/tracking/compliance?groupBy=nonsense")).StatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync("/api/v1/tracking/reports/compliance?format=csv")).IsSuccessStatusCode.ShouldBeTrue();
    }
}
