using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Execution;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using Xunit;

namespace LogiVue.Tms.Tests;

/// <summary>
/// Claims, cost and availability flow into the KPIs; vendors manage their own drivers and capacity; rankings and
/// benchmarks follow the stored KPIs.
/// </summary>
public class OperationalSourcesApiTests : IClassFixture<TestHost>
{
    private const string AllRoles = "Transport Admin,Transport Manager,Transport Executive,Compliance User,Finance User,Operations User";
    private const string VendorRoles = "Transporter Admin,Transporter Operations User,Transporter Viewer";
    private const string VendorViewerRoles = "Transporter Viewer";

    private static readonly DateOnly JuneStart = new(2026, 6, 1);
    private static readonly DateOnly JuneEnd = new(2026, 6, 30);

    private readonly TestHost _host;
    private readonly HttpClient _admin;

    public OperationalSourcesApiTests(TestHost host)
    {
        _host = host;
        _host.EnsureTransporterSchemaAsync().GetAwaiter().GetResult();
        _admin = Internal(AllRoles);
    }

    private HttpClient Internal(string roles)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, "ops");
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, roles);
        return client;
    }

    private HttpClient Vendor(long transporterId, string roles = VendorRoles)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.UserHeader, $"vendor-{transporterId}");
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.TransporterHeader, transporterId.ToString());
        client.DefaultRequestHeaders.Add(HeaderCurrentUser.RolesHeader, roles);
        return client;
    }

    [Fact]
    public async Task Claims_cost_and_capacity_flow_into_the_month_s_kpis()
    {
        var transporterId = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Metrics Co", 40000m, OtdPct: null));
        await SeedDeliveriesAsync(transporterId, count: 4);

        var claim = await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/claims",
            new { claimType = "Damage", claimDate = "2026-06-15", claimValue = 5000m, loadReference = "CL-1", remarks = "Crushed carton" }, TestHost.Json);
        claim.StatusCode.Should().Be(HttpStatusCode.Created);

        await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/load-costs",
            new { loadReference = "COST-1", serviceDate = "2026-06-10", agreedAmount = 1000m, invoicedAmount = 900m }, TestHost.Json);
        await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/load-costs",
            new { loadReference = "COST-2", serviceDate = "2026-06-11", agreedAmount = 1000m, invoicedAmount = 1200m }, TestHost.Json);

        var vendor = Vendor(transporterId);
        (await vendor.PutAsJsonAsync("/api/v1/vendor/capacity", new { date = "2026-06-10", vehiclesCommitted = 10, vehiclesAvailable = 8 }, TestHost.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await vendor.PutAsJsonAsync("/api/v1/vendor/capacity", new { date = "2026-06-11", vehiclesCommitted = 10, vehiclesAvailable = 10 }, TestHost.Json))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var kpis = await OperationsKpisAsync(transporterId);
        kpis[KpiType.ClaimsRate].Should().Match<(decimal Numerator, decimal Denominator, decimal? Value)>(k => k.Numerator == 1 && k.Denominator == 4 && k.Value == 25m);
        kpis[KpiType.CostPerformance].Should().Match<(decimal Numerator, decimal Denominator, decimal? Value)>(k => k.Numerator == 1 && k.Denominator == 2 && k.Value == 50m);
        kpis[KpiType.Availability].Should().Match<(decimal Numerator, decimal Denominator, decimal? Value)>(k => k.Numerator == 18 && k.Denominator == 20 && k.Value == 90m);
    }

    [Fact]
    public async Task Resolving_a_claim_twice_is_a_conflict_and_a_future_claim_is_refused()
    {
        var transporterId = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Claims Co", 40000m, OtdPct: null));

        var future = await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/claims",
            new { claimType = "Shortage", claimDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), claimValue = 100m }, TestHost.Json);
        future.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var created = await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/claims",
            new { claimType = "LossTheft", claimDate = "2026-06-20", claimValue = 250m }, TestHost.Json);
        var id = (await created.Content.ReadFromJsonAsync<ClaimResponse>(TestHost.Json))!.Id;

        (await _admin.PostAsync($"/api/v1/transporters/{transporterId}/claims/{id}/resolve", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await _admin.PostAsync($"/api/v1/transporters/{transporterId}/claims/{id}/resolve", null);
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_load_can_carry_only_one_cost_record()
    {
        var transporterId = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Cost Co", 40000m, OtdPct: null));
        var body = new { loadReference = "DUP-1", serviceDate = "2026-06-05", agreedAmount = 500m, invoicedAmount = 500m };

        (await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/load-costs", body, TestHost.Json)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await _admin.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/load-costs", body, TestHost.Json)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Capacity_cannot_show_more_available_vehicles_than_committed()
    {
        var transporterId = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Capacity Co", 40000m, OtdPct: null));

        var response = await Vendor(transporterId).PutAsJsonAsync("/api/v1/vendor/capacity",
            new { date = "2026-06-12", vehiclesCommitted = 5, vehiclesAvailable = 6 }, TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Vendors_cannot_reach_internal_claims_or_costs()
    {
        var transporterId = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Isolation Co", 40000m, OtdPct: null));
        var vendor = Vendor(transporterId);

        (await vendor.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/claims",
            new { claimType = "Damage", claimDate = "2026-06-15", claimValue = 10m }, TestHost.Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await vendor.PostAsJsonAsync($"/api/v1/transporters/{transporterId}/load-costs",
            new { loadReference = "X", serviceDate = "2026-06-15", agreedAmount = 10m, invoicedAmount = 10m }, TestHost.Json)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_vendor_manages_its_own_drivers_and_cannot_touch_another_transporters()
    {
        var mine = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Driver Vendor", 40000m, OtdPct: null));
        var other = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Other Vendor", 40000m, OtdPct: null));
        var vendor = Vendor(mine);

        var added = await vendor.PostAsJsonAsync("/api/v1/vendor/drivers",
            new { fullName = "Ravi Kumar", mobile = "+91 98200 55555", licenceNumber = "MH12 20230000123" }, TestHost.Json);
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        var driver = (await added.Content.ReadFromJsonAsync<DriverResponse>(TestHost.Json))!;

        var listed = await (await vendor.GetAsync("/api/v1/vendor/drivers")).Content.ReadFromJsonAsync<List<DriverResponse>>(TestHost.Json);
        listed!.Should().Contain(d => d.Id == driver.Id && d.TransporterId == mine);

        // Another vendor cannot edit this driver, and the update cannot change the driver's status.
        var intruder = await Vendor(other).PutAsJsonAsync($"/api/v1/vendor/drivers/{driver.Id}",
            new { fullName = "Changed", mobile = "+91 98200 55555", licenceNumber = "MH12 20230000123" }, TestHost.Json);
        intruder.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var updated = await vendor.PutAsJsonAsync($"/api/v1/vendor/drivers/{driver.Id}",
            new { fullName = "Ravi K.", mobile = "+91 98200 55555", licenceNumber = "MH12 20230000123", status = "Inactive" }, TestHost.Json);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<DriverResponse>(TestHost.Json))!.Status.Should().Be(RecordStatus.Active);
    }

    [Fact]
    public async Task A_viewer_cannot_add_drivers_for_the_vendor()
    {
        var transporterId = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile("Viewer Vendor", 40000m, OtdPct: null));

        var response = await Vendor(transporterId, VendorViewerRoles).PostAsJsonAsync("/api/v1/vendor/drivers",
            new { fullName = "Viewer Driver", mobile = "+91 98200 11111", licenceNumber = "KA01 20230000999" }, TestHost.Json);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Ranking_orders_by_the_metric_and_leaves_thin_samples_unranked()
    {
        var strong = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile($"Rank Strong {Guid.NewGuid():N}", 40000m, OtdPct: 96.4m));
        var weak = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile($"Rank Weak {Guid.NewGuid():N}", 40000m, OtdPct: 81.2m));
        var thin = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile($"Rank Thin {Guid.NewGuid():N}", 40000m, OtdPct: 99.9m, OtdSample: 5m));

        var response = await _admin.GetAsync("/api/v1/transporters/rankings?from=2026-07-01&to=2026-09-30&metric=OnTimeDelivery");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await response.Content.ReadFromJsonAsync<RankingResponse>(TestHost.Json))!.Rows;

        var strongRow = rows.Single(r => r.TransporterId == strong);
        var weakRow = rows.Single(r => r.TransporterId == weak);
        var thinRow = rows.Single(r => r.TransporterId == thin);

        strongRow.Ranked.Should().BeTrue();
        weakRow.Ranked.Should().BeTrue();
        strongRow.Rank!.Should().BeLessThan(weakRow.Rank!.Value);

        // A sample of 5 is below the minimum of 20, so the 99.9% figure is shown but not ranked.
        thinRow.Ranked.Should().BeFalse();
        thinRow.Rank.Should().BeNull();
        thinRow.Note.Should().Contain("minimum");
    }

    [Fact]
    public async Task Ranking_needs_a_full_lane_and_no_mixed_scope()
    {
        var half = await _admin.GetAsync("/api/v1/transporters/rankings?from=2026-07-01&to=2026-09-30&originLocationReference=100001");
        half.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var mixed = await _admin.GetAsync("/api/v1/transporters/rankings?from=2026-07-01&to=2026-09-30&originLocationReference=100001&destinationLocationReference=100002&vehicleTypeReference=7");
        mixed.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var noPeriod = await _admin.GetAsync("/api/v1/transporters/rankings");
        noPeriod.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Benchmark_gaps_are_signed_so_negative_means_behind_including_for_claims()
    {
        var subject = await TransporterFixtures.SeedAsync(_host, FixtureLane.Unique(), new FixtureProfile($"Bench Subject {Guid.NewGuid():N}", 40000m, OtdPct: 96.4m));

        var response = await _admin.GetAsync($"/api/v1/transporters/benchmark?transporterId={subject}&from=2026-07-01&to=2026-09-30");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await response.Content.ReadFromJsonAsync<BenchmarkResponse>(TestHost.Json))!.Rows;

        var otd = rows.Single(r => r.Kpi == KpiType.OnTimeDelivery);
        otd.Transporter.Should().Be(96.4m);
        otd.TopPerformer.Should().BeGreaterThanOrEqualTo(96.4m);
        otd.GapToTop.Should().BeLessThanOrEqualTo(0m);
        otd.RegionAverage.Should().BeNull("the transporter has no state, so it has no region peers");
        otd.LaneAverage.Should().BeNull("no lane-level KPIs exist for the transporter in this period");

        // Claims are lower-is-better: the transporter is at or above the best (lowest) rate, so the gap cannot be positive.
        var claims = rows.Single(r => r.Kpi == KpiType.ClaimsRate);
        claims.GapToTop.Should().BeLessThanOrEqualTo(0m);
    }

    [Fact]
    public async Task Benchmark_for_an_unknown_transporter_is_not_found()
    {
        var response = await _admin.GetAsync("/api/v1/transporters/benchmark?transporterId=999999999&from=2026-07-01&to=2026-09-30");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Four loads delivered in June, which is the claims and availability denominator. Pickups are not needed.</summary>
    private async Task SeedDeliveriesAsync(long transporterId, int count)
    {
        await _host.WithTransporterDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                db.LoadExecutions.Add(new LoadExecution
                {
                    LoadReference = $"DEL-{transporterId}-{i}-{Guid.NewGuid():N}"[..40],
                    TransporterId = transporterId,
                    TenderId = 0,
                    ActualDeliveryAt = new DateTime(2026, 6, 5 + i, 12, 0, 0, DateTimeKind.Utc),
                    Status = ExecutionStatus.Delivered,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }

            await db.SaveChangesAsync();
            return true;
        });
    }

    private async Task<Dictionary<KpiType, (decimal Numerator, decimal Denominator, decimal? Value)>> OperationsKpisAsync(long transporterId)
    {
        var response = await _admin.GetAsync($"/api/v1/transporters/performance/{transporterId}/operations?from={JuneStart:yyyy-MM-dd}&to={JuneEnd:yyyy-MM-dd}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<OperationsResponse>(TestHost.Json))!;
        return body.Kpis.ToDictionary(k => k.Type, k => (k.Numerator, k.Denominator, k.Value));
    }

    private sealed record ClaimResponse(long Id);

    private sealed record DriverResponse(long Id, long TransporterId, string FullName, string Mobile, string LicenceNumber, RecordStatus Status);

    private sealed record OperationsKpi(KpiType Type, decimal Numerator, decimal Denominator, decimal? Value);

    private sealed record OperationsResponse(IReadOnlyList<OperationsKpi> Kpis);

    private sealed record RankedRow(int? Rank, long TransporterId, bool Ranked, string? Note);

    private sealed record RankingResponse(IReadOnlyList<RankedRow> Rows);

    private sealed record BenchmarkRow(KpiType Kpi, decimal? Transporter, decimal? LaneAverage, decimal? RegionAverage, decimal? TopPerformer, decimal? GapToTop);

    private sealed record BenchmarkResponse(IReadOnlyList<BenchmarkRow> Rows);
}
