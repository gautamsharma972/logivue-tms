using Tms.Modules.Contracts.Domain;
using static Tms.UnitTests.Contracts.ContractTestData;

namespace Tms.UnitTests.Contracts;

public class FuelClauseTests
{
    [Fact]
    public void Rupee_steps_move_freight_by_the_impact_per_whole_step()
    {
        var adjustment = Fuel().Evaluate(95m); // ₹5 above base, ₹1 per step, 0.5% per step

        adjustment.Steps.ShouldBe(5);
        adjustment.Percent.ShouldBe(2.5m);
    }

    [Fact]
    public void A_partial_step_does_not_count()
    {
        Fuel().Evaluate(94.99m).Percent.ShouldBe(2.0m);
        Fuel().Evaluate(90.99m).Percent.ShouldBe(0m);
    }

    [Fact]
    public void Percent_steps_are_measured_against_the_base_price()
    {
        // Base 100, price 105 = +5%; 1% per step; 0.5% freight per step.
        var adjustment = Fuel(FuelStepUnit.Percent, basePrice: 100m).Evaluate(105m);

        adjustment.Steps.ShouldBe(5);
        adjustment.Percent.ShouldBe(2.5m);
    }

    [Fact]
    public void De_escalation_reduces_freight_when_the_clause_is_two_way()
    {
        Fuel().Evaluate(85m).Percent.ShouldBe(-2.5m);
    }

    [Fact]
    public void An_escalation_only_clause_never_goes_below_the_base_rate()
    {
        Fuel(direction: FuelDirection.EscalationOnly).Evaluate(80m).Percent.ShouldBe(0m);
        Fuel(direction: FuelDirection.EscalationOnly).Evaluate(92m).Percent.ShouldBe(1.0m);
    }

    [Fact]
    public void The_dead_band_ignores_small_movements_but_counts_everything_once_exceeded()
    {
        var clause = Fuel(deadBand: 3m);

        clause.Evaluate(92.5m).Percent.ShouldBe(0m);
        clause.Evaluate(93m).Percent.ShouldBe(1.5m, "3 steps once the band is reached, not 0");
    }

    [Fact]
    public void The_cap_limits_the_adjustment_in_both_directions()
    {
        var clause = Fuel(cap: 5m);

        clause.Evaluate(130m).Percent.ShouldBe(5m);
        clause.Evaluate(50m).Percent.ShouldBe(-5m);
        clause.Evaluate(130m).Explanation.ShouldContain("capped");
    }

    [Fact]
    public void The_explanation_says_what_happened()
    {
        Fuel().Evaluate(95m).Explanation.ShouldContain("+2.5%");
        Fuel().Evaluate(95m).Explanation.ShouldContain("₹95.00 vs base ₹90.00");
    }

    [Theory]
    [InlineData("", 90, 1, 0.5)]
    [InlineData("Delhi", 0, 1, 0.5)]
    [InlineData("Delhi", 90, 0, 0.5)]
    [InlineData("Delhi", 90, 1, 0)]
    [InlineData("Delhi", 90, 1, 101)]
    public void Invalid_clauses_are_rejected(string region, double basePrice, double step, double impact)
    {
        new FuelClause(region, (decimal)basePrice, FuelStepUnit.Rupees, (decimal)step, (decimal)impact, 0, null, FuelDirection.Both)
            .Validate().IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_diesel_price_in_force_is_the_latest_one_that_has_started()
    {
        var prices = new[]
        {
            DieselPrice.Create(Tenant, "delhi", new DateOnly(2026, 1, 1), 90m).Value,
            DieselPrice.Create(Tenant, "Delhi", new DateOnly(2026, 6, 1), 95m).Value,
            DieselPrice.Create(Tenant, "Delhi", new DateOnly(2026, 7, 1), 99m).Value,
            DieselPrice.Create(Tenant, "Mumbai", new DateOnly(2026, 1, 1), 104m).Value,
        };

        DieselPrice.Resolve(prices, " DELHI ", new DateOnly(2026, 6, 15)).ShouldBe(95m);
        DieselPrice.Resolve(prices, "Delhi", new DateOnly(2026, 6, 1)).ShouldBe(95m, "effective on its first day");
        DieselPrice.Resolve(prices, "Delhi", new DateOnly(2025, 12, 31)).ShouldBeNull();
        DieselPrice.Resolve(prices, "Chennai", new DateOnly(2026, 6, 15)).ShouldBeNull();
    }

    [Fact]
    public void Diesel_prices_are_validated()
    {
        DieselPrice.Create(Tenant, "", new DateOnly(2026, 1, 1), 90m).IsFailure.ShouldBeTrue();
        DieselPrice.Create(Tenant, "Delhi", new DateOnly(2026, 1, 1), 0m).IsFailure.ShouldBeTrue();
        DieselPrice.Create(Tenant, "Delhi", new DateOnly(2026, 1, 1), 94.567m).Value.PricePerLitre.ShouldBe(94.57m);
    }
}

public class PlaceAndZoneTests
{
    [Fact]
    public void Places_match_regardless_of_case_and_spacing()
    {
        Place.OfCity("MAHARASHTRA", "pune").Matches(Pune, NoZones).ShouldBeTrue();
        Place.OfCity("Maharashtra", "Mumbai").Matches(Mumbai, NoZones).ShouldBeTrue("input was ' mumbai ' / 'maharashtra'");
        Place.OfCity("Maharashtra", "Pune").Matches(Mumbai, NoZones).ShouldBeFalse();
        Place.OfState("Maharashtra").Matches(Pune, NoZones).ShouldBeTrue();
        Place.OfState("Gujarat").Matches(Pune, NoZones).ShouldBeFalse();
        Place.Anywhere.Matches(Delhi, NoZones).ShouldBeTrue();
    }

    [Fact]
    public void A_city_with_the_same_name_in_another_state_is_a_different_place()
    {
        Place.OfCity("Madhya Pradesh", "Aurangabad").Matches(new Location("Maharashtra", "Aurangabad"), NoZones).ShouldBeFalse();
    }

    [Fact]
    public void Specificity_orders_city_over_zone_over_state_over_anywhere()
    {
        var order = new[] { Place.Anywhere, Place.OfState("X"), Place.OfZone("Z"), Place.OfCity("X", "Y") }.Select(p => p.Specificity);

        order.ShouldBe(order.Order());
        order.Distinct().Count().ShouldBe(4);
    }

    [Fact]
    public void A_zone_matches_whole_states_and_individual_cities()
    {
        var zone = Zone.Create(Tenant, "west 1", "Western", [new ZoneMember("Gujarat", null), new ZoneMember("Maharashtra", "Pune")]).Value;
        var zones = Zones(zone);

        zone.Code.ShouldBe("WEST_1");
        Place.OfZone("west_1").Matches(Surat, zones).ShouldBeTrue("any city in Gujarat");
        Place.OfZone("WEST_1").Matches(Pune, zones).ShouldBeTrue();
        Place.OfZone("WEST_1").Matches(Mumbai, zones).ShouldBeFalse("only Pune is listed for Maharashtra");
        Place.OfZone("MISSING").Matches(Pune, zones).ShouldBeFalse("an unknown zone matches nothing");
    }

    [Fact]
    public void Zones_are_validated()
    {
        Zone.Create(Tenant, "bad code!", "X", [new ZoneMember("Goa", null)]).IsFailure.ShouldBeTrue();
        Zone.Create(Tenant, "OK", " ", [new ZoneMember("Goa", null)]).IsFailure.ShouldBeTrue();
        Zone.Create(Tenant, "OK", "Name", []).IsFailure.ShouldBeTrue();
        Zone.Create(Tenant, "OK", "Name", [new ZoneMember("goa", null), new ZoneMember("GOA", null)]).Value.Members.Count.ShouldBe(1);
    }

    [Fact]
    public void Places_need_the_parts_their_kind_requires()
    {
        Place.From(PlaceKind.City, "Goa", null, null).IsFailure.ShouldBeTrue();
        Place.From(PlaceKind.State, null, null, null).IsFailure.ShouldBeTrue();
        Place.From(PlaceKind.Zone, null, null, " ").IsFailure.ShouldBeTrue();
        Place.From(PlaceKind.Any, null, null, null).IsSuccess.ShouldBeTrue();
    }
}
