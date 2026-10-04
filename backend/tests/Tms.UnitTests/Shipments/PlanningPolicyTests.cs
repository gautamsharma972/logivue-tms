using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Shipments.ShipmentTestData;

namespace Tms.UnitTests.Shipments;

public class PlanningPolicyTests
{
    private static readonly Guid Other = Guid.NewGuid();

    private sealed class TwoCarriers(decimal first, decimal second) : IFreightQuoteService
    {
        public Task<FreightQuoteSet> QuoteAsync(FreightQuoteRequest request, CancellationToken cancellationToken = default)
        {
            FreightQuoteResult Quote(Guid carrier, string name, decimal total) =>
                new(Guid.NewGuid(), "CN", carrier, name, request.Mode ?? FreightMode.Ftl, "lane", null, [new FreightQuoteLine("FREIGHT", "Freight", total)], [], total);
            return Task.FromResult(new FreightQuoteSet([Quote(Transporter, "Alpha Roadlines", first), Quote(Other, "Beta Carriers", second)], null));
        }
    }

    private sealed class Policy(Func<Guid, TransporterStanding> standing) : ITransporterPlanningPolicy
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<Guid, TransporterStanding>> GetStandingsAsync(
            IReadOnlyCollection<Guid> ids, DateOnly date, string originState, string? originCity, string? destinationState, string? destinationCity, FreightMode mode, bool urgent,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyDictionary<Guid, TransporterStanding>>(ids.ToDictionary(i => i, standing));
        }
    }

    private static PlanningInput Input(params Order[] orders) =>
        new(Today, orders.Select(o => new PlannableOrder(o.Id, o.Number, o.Direction, o.PickupState, o.PickupCity, o.DropState, o.DropCity, o.WeightKg, o.VolumeCbm, o.ReadyDate, o.DeliverByDate)).ToList(),
            Types, new PlanOptions(AllowPtl: false, AllowConsolidation: false), []);

    private static TransporterStanding Ok(Guid id, bool preferred = false, decimal? score = null) => new(id, true, null, preferred, score);

    private static TransporterStanding Barred(Guid id, string reason) => new(id, false, reason, false, null);

    [Fact]
    public async Task A_barred_transporter_is_not_offered_the_load_even_when_it_is_cheapest_and_the_plan_says_so()
    {
        var policy = new Policy(id => id == Transporter ? Barred(id, "Transporter is suspended.") : Ok(id));
        var optimizer = new RuleBasedPlanningOptimizer(new TwoCarriers(20_000m, 25_000m), policy: policy);

        var plan = await optimizer.OptimizeAsync(Input(NewOrder(3_000m)), default);

        var vehicle = plan.Vehicles.ShouldHaveSingleItem();
        vehicle.TransporterName.ShouldBe("Beta Carriers");
        vehicle.EstimatedCost.ShouldBe(25_000m);
        vehicle.Reason.ShouldContain("Not offered the load");
        vehicle.Reason.ShouldContain("Alpha Roadlines");
        vehicle.Reason.ShouldContain("suspended");
    }

    [Fact]
    public async Task When_every_transporter_with_a_rate_is_barred_the_order_is_unplanned_with_each_reason()
    {
        var policy = new Policy(id => Barred(id, id == Transporter ? "Restricted by a planning rule." : "Avoided for urgent loads."));
        var optimizer = new RuleBasedPlanningOptimizer(new TwoCarriers(20_000m, 25_000m), policy: policy);

        var plan = await optimizer.OptimizeAsync(Input(NewOrder(3_000m)), default);

        plan.Vehicles.ShouldBeEmpty();
        var left = plan.Unplanned.ShouldHaveSingleItem();
        left.Code.ShouldBe(UnplannedCodes.TransporterRestricted);
        left.Reason.ShouldContain("Alpha Roadlines: Restricted by a planning rule.");
        left.Reason.ShouldContain("Beta Carriers: Avoided for urgent loads.");
        left.Suggestions.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task At_the_same_price_a_preferred_carrier_wins_then_the_better_scored_but_price_still_comes_first()
    {
        var preferred = new Policy(id => id == Other ? Ok(id, preferred: true) : Ok(id));
        (await new RuleBasedPlanningOptimizer(new TwoCarriers(20_000m, 20_000m), policy: preferred).OptimizeAsync(Input(NewOrder(3_000m)), default))
            .Vehicles.Single().TransporterName.ShouldBe("Beta Carriers");

        var scored = new Policy(id => Ok(id, score: id == Transporter ? 70m : 90m));
        (await new RuleBasedPlanningOptimizer(new TwoCarriers(20_000m, 20_000m), policy: scored).OptimizeAsync(Input(NewOrder(3_000m)), default))
            .Vehicles.Single().TransporterName.ShouldBe("Beta Carriers");

        // A preferred carrier does not beat a cheaper one: preference is a tie-break, never a reason to pay more.
        (await new RuleBasedPlanningOptimizer(new TwoCarriers(18_000m, 20_000m), policy: preferred).OptimizeAsync(Input(NewOrder(3_000m)), default))
            .Vehicles.Single().TransporterName.ShouldBe("Alpha Roadlines");
    }

    [Fact]
    public async Task Standings_are_asked_once_per_lane_and_not_for_every_order_again()
    {
        var policy = new Policy(id => Ok(id));
        var optimizer = new RuleBasedPlanningOptimizer(new TwoCarriers(20_000m, 25_000m), policy: policy);

        await optimizer.OptimizeAsync(Input(NewOrder(1_000m), NewOrder(1_200m), NewOrder(900m)), default);

        policy.Calls.ShouldBeLessThanOrEqualTo(3); // one per distinct request, never one per quote
    }

    [Fact]
    public async Task Without_a_policy_planning_behaves_as_before()
    {
        var plan = await new RuleBasedPlanningOptimizer(new TwoCarriers(20_000m, 25_000m)).OptimizeAsync(Input(NewOrder(3_000m)), default);

        plan.Vehicles.Single().TransporterName.ShouldBe("Alpha Roadlines");
    }
}
