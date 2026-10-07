using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Contracts.ContractTestData;
using static Tms.UnitTests.Contracts.RatingTestData;

namespace Tms.UnitTests.Contracts;

public class RatingEngineTests
{
    private static RatingOutcome Run(IEnumerable<Contract> contracts, RatingInput? input = null, RatingContext? context = null) =>
        RatingEngine.Rate(contracts, input ?? Input(vehicle: Truck32, km: 155m, kg: 12_500m), context ?? Context());

    [Fact]
    public void The_contract_example_is_rated_at_forty_one_thousand_three_hundred_and_twenty_and_explains_itself()
    {
        var rate = MumbaiPune(38_000m, new RateExtras(Code: "RATE-MUM-PUN-32FT", MinWeightKg: 10_000m, MaxWeightKg: 15_000m), minKm: 150m, maxKm: 200m);
        var toll = new AccessorialSpec("TOLL", "Toll", AccessorialCalc.Reimbursed, "TRIP");
        var contract = ActiveWith([rate], dph: [Dph()], accessorials: [toll], number: "CNT-ABC-2026");

        var outcome = Run([contract], Input(vehicle: Truck32, kg: 12_500m, cbm: 42m, km: 155m, inputs: new() { ["TOLL"] = 1_800m }), Context(prices: [Diesel(102m, Today.AddDays(-5))]));

        outcome.Qualified.ShouldBeTrue();
        var best = outcome.Selected!;
        best.BaseFreight.ShouldBe(38_000m);
        best.Dph.ShouldBe(1_520m);
        best.Accessorials.ShouldBe(1_800m);
        best.Total.ShouldBe(41_320m);
        best.Lines.Sum(l => l.Amount).ShouldBe(best.Total, "the total is exactly the lines shown");
        best.Reasons.ShouldContain(r => r.Contains("exact lane"));
        best.Reasons.ShouldContain(r => r.Contains("Weight slab 10000–15000"));
        best.Reasons.ShouldContain(r => r.Contains("Distance slab 150–200"));
        outcome.Trace.Select(t => t.Stage).ShouldBe(["Input", "Contract", "Base freight", "DPH", "Accessorials", "Result"], ignoreOrder: false);
    }

    [Fact]
    public void An_exact_lane_beats_a_zone_which_beats_a_state_which_beats_the_default()
    {
        var west = Zone.Create(Tenant, "WEST", "West", [new ZoneMember("Maharashtra", null)]).Value;
        var contract = ActiveWith(
        [
            Rate(Place.Anywhere, Place.Anywhere, new FlatTripPricing(100m), Truck32, minKm: 0, maxKm: 5000),
            Rate(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), new FlatTripPricing(200m), Truck32),
            Rate(Place.OfZone("WEST"), Place.OfZone("WEST"), new FlatTripPricing(300m), Truck32),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(400m), Truck32),
        ]);
        var ctx = Context([west]);

        Run([contract], Input(vehicle: Truck32, km: 150m), ctx).Selected!.BaseFreight.ShouldBe(400m);
        Run([contract], Input(Mumbai, new Location("Maharashtra", "Nagpur"), vehicle: Truck32, km: 800m), ctx).Selected!.BaseFreight.ShouldBe(300m);
        Run([contract], Input(Pune, Surat, vehicle: Truck32, km: 400m), ctx).Selected!.BaseFreight.ShouldBe(100m);
        var loser = Run([contract], Input(vehicle: Truck32, km: 150m), ctx).Exclusions.Single(e => e.Reason.StartsWith("zone", StringComparison.Ordinal));
        loser.Code.ShouldBe("LOWER_PRIORITY");
    }

    [Fact]
    public void With_no_exact_lane_the_zone_rate_is_used_and_the_result_says_so()
    {
        var west = Zone.Create(Tenant, "WEST", "West", [new ZoneMember("Maharashtra", null), new ZoneMember("Gujarat", null)]).Value;
        var contract = ActiveWith([Rate(Place.OfZone("WEST"), Place.OfZone("WEST"), new FlatTripPricing(42_000m), Truck32)]);

        var outcome = Run([contract], Input(Mumbai, Surat, vehicle: Truck32, km: 280m), Context([west]));

        outcome.Selected!.BaseFreight.ShouldBe(42_000m);
        outcome.Selected.Reasons.ShouldContain(r => r.StartsWith("✓ zone", StringComparison.Ordinal));
    }

    [Fact]
    public void Rates_that_were_not_used_are_listed_with_the_reason()
    {
        var contract = ActiveWith(
        [
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), Truck32, new RateExtras(Code: "GOOD")),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(35_000m), Truck14, new RateExtras(Code: "SMALL")),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(30_000m), Truck32, new RateExtras(Code: "OLD", ValidTo: Today.AddDays(-1), ValidFrom: Today.AddDays(-100))),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(31_000m), Truck32, new RateExtras(Code: "LATER", ValidFrom: Today.AddDays(30))),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(32_000m), Truck32, new RateExtras(Code: "HEAVY", MinWeightKg: 20_000m, MaxWeightKg: 25_000m)),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(33_000m), Truck32, new RateExtras(Code: "FAR"), minKm: 300m, maxKm: 400m),
        ]);

        var outcome = Run([contract], Input(vehicle: Truck32, kg: 12_500m, km: 155m));

        outcome.Selected!.Card.Code.ShouldBe("GOOD");
        string Why(string code) => outcome.Exclusions.Single(e => e.RateCode!.StartsWith(code, StringComparison.Ordinal)).Code;
        Why("SMALL").ShouldBe("VEHICLE_MISMATCH");
        Why("OLD").ShouldBe("RATE_EXPIRED");
        Why("LATER").ShouldBe("RATE_NOT_YET_VALID");
        Why("HEAVY").ShouldBe("WEIGHT_SLAB_MISMATCH");
        Why("FAR").ShouldBe("DISTANCE_SLAB_MISMATCH");
    }

    [Fact]
    public void No_rate_is_a_clear_result_with_advice_never_a_zero_freight()
    {
        var contract = ActiveWith([Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), Truck32)]);

        var outcome = Run([contract], Input(Delhi, Surat, vehicle: Truck32, km: 1_200m, kg: 12_500m));

        outcome.Qualified.ShouldBeFalse();
        outcome.ErrorCode.ShouldBe("FREIGHT_RATE_NOT_FOUND");
        outcome.Message!.ShouldContain("No applicable active freight rate");
        outcome.Message!.ShouldContain("12500");
        outcome.Advice.ShouldContain(a => a.Contains("zone rate"));
        outcome.Selected.ShouldBeNull();
        outcome.Options.ShouldBeEmpty();
    }

    [Fact]
    public void Two_rates_that_cannot_be_told_apart_are_a_conflict_not_a_coin_toss()
    {
        var contract = ActiveWith(
        [
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), Truck32, new RateExtras(Code: "A", MinWeightKg: 10_000m, MaxWeightKg: 15_000m)),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(39_000m), Truck32, new RateExtras(Code: "B", MinWeightKg: 12_000m, MaxWeightKg: 18_000m)),
        ]);

        var outcome = Run([contract], Input(vehicle: Truck32, km: 155m, kg: 13_000m));

        outcome.Qualified.ShouldBeFalse();
        outcome.ErrorCode.ShouldBe("FREIGHT_RATE_CONFLICT");
        outcome.Exclusions.Count(e => e.Code == "RATE_CONFLICT").ShouldBe(2);
        outcome.Selected.ShouldBeNull();
    }

    [Fact]
    public void A_priority_decides_between_rates_that_both_qualify()
    {
        var contract = ActiveWith(
        [
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), Truck32, new RateExtras(Code: "STANDARD", Priority: 100)),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(39_000m), Truck32, new RateExtras(Code: "PREFERRED", Priority: 10)),
        ]);

        var outcome = Run([contract]);

        outcome.Selected!.Card.Code.ShouldBe("PREFERRED");
        outcome.Exclusions.Single().Reason.ShouldContain("Preference 100 is lower than preference 10");
    }

    [Fact]
    public void A_rate_with_a_slab_condition_beats_one_with_none_when_both_qualify()
    {
        var contract = ActiveWith(
        [
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(40_000m), Truck32, new RateExtras(Code: "GENERAL")),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), Truck32, new RateExtras(Code: "BANDED", MinWeightKg: 10_000m, MaxWeightKg: 15_000m)),
        ]);

        Run([contract], Input(vehicle: Truck32, km: 155m, kg: 12_500m)).Selected!.Card.Code.ShouldBe("BANDED");
        Run([contract], Input(vehicle: Truck32, km: 155m, kg: 20_000m)).Selected!.Card.Code.ShouldBe("GENERAL");
    }

    [Fact]
    public void A_rate_that_needs_a_special_service_applies_only_to_shipments_that_need_it()
    {
        var contract = ActiveWith(
        [
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), Truck32, new RateExtras(Code: "NORMAL")),
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(46_000m), Truck32, new RateExtras(Code: "HAZ", RequiredCapabilities: ["HAZMAT"])),
        ]);

        Run([contract], Input(vehicle: Truck32, km: 155m)).Selected!.Card.Code.ShouldBe("NORMAL");
        Run([contract], Input(vehicle: Truck32, km: 155m, capabilities: ["hazmat"])).Selected!.Card.Code.ShouldBe("HAZ");
    }

    [Fact]
    public void A_both_ways_rate_also_prices_the_return_lane()
    {
        var contract = ActiveWith([Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), Truck32, bothWays: true)]);

        Run([contract], Input(Pune, Mumbai, vehicle: Truck32, km: 155m)).Selected!.BaseFreight.ShouldBe(38_000m);
    }

    [Fact]
    public void A_contract_not_in_force_is_excluded_with_the_reason()
    {
        var rates = new[] { MumbaiPune(38_000m) };
        var expired = ActiveWith(rates, from: Today.AddDays(-400), to: Today.AddDays(-35), number: "CN-1");
        var future = ActiveWith(rates, from: Today.AddDays(10), to: Today.AddDays(400), number: "CN-2");
        var draft = Draft();
        draft.ReplaceRates(rates);
        var suspended = ActiveWith(rates, number: "CN-3");
        suspended.Suspend("Dispute", Today.AddDays(-2)).IsSuccess.ShouldBeTrue();

        var outcome = Run([expired, future, draft, suspended], Input(vehicle: Truck32, km: 155m));

        outcome.Qualified.ShouldBeFalse();
        outcome.Exclusions.Select(e => e.Code).ShouldBe(["CONTRACT_EXPIRED", "CONTRACT_NOT_YET_EFFECTIVE", "CONTRACT_NOT_ACTIVE", "CONTRACT_SUSPENDED"], ignoreOrder: true);
    }

    [Fact]
    public void A_preview_rates_against_a_draft_ignoring_its_status()
    {
        var draft = Draft();
        draft.ReplaceRates([MumbaiPune(38_000m)]).IsSuccess.ShouldBeTrue();

        Run([draft], context: Context(requireInForce: false)).Selected!.BaseFreight.ShouldBe(38_000m);
    }

    [Fact]
    public void The_contract_version_in_force_on_the_shipment_date_is_the_one_that_prices_it()
    {
        var v1 = ActiveWith([MumbaiPune(38_000m)], from: new DateOnly(2026, 1, 1), to: new DateOnly(2026, 6, 30), number: "CNT-ABC-2026");
        var v2 = ActiveWith([MumbaiPune(40_000m)], from: new DateOnly(2026, 7, 1), to: new DateOnly(2026, 12, 31), number: "CNT-ABC-2026");

        Run([v1, v2], Input(vehicle: Truck32, km: 155m, date: new DateOnly(2026, 5, 10))).Selected!.BaseFreight.ShouldBe(38_000m);
        Run([v1, v2], Input(vehicle: Truck32, km: 155m, date: new DateOnly(2026, 7, 10))).Selected!.BaseFreight.ShouldBe(40_000m);
    }

    [Fact]
    public void Transporters_are_ranked_by_preference_then_by_freight()
    {
        var a = ActiveWith([MumbaiPune(40_000m)], transporter: Guid.NewGuid(), number: "CN-A");
        var b = ActiveWith([MumbaiPune(38_000m)], transporter: Guid.NewGuid(), number: "CN-B");
        var preferred = ActiveWith([MumbaiPune(41_000m, new RateExtras(Priority: 10))], transporter: Guid.NewGuid(), number: "CN-C");

        var cheapestFirst = Run([a, b]);
        var preferenceFirst = Run([a, b, preferred]);

        cheapestFirst.Options.Select(o => o.Total).ShouldBe([38_000m, 40_000m]);
        preferenceFirst.Options[0].Contract.Number.ShouldBe("CN-C");
        preferenceFirst.Options.Count.ShouldBe(3);
    }

    [Fact]
    public void Naming_a_transporter_leaves_out_the_others()
    {
        var mine = Guid.NewGuid();
        var a = ActiveWith([MumbaiPune(40_000m)], transporter: mine, number: "CN-A");
        var b = ActiveWith([MumbaiPune(38_000m)], transporter: Guid.NewGuid(), number: "CN-B");

        var outcome = Run([a, b], Input(vehicle: Truck32, km: 155m, transporter: mine));

        outcome.Options.Single().Contract.Number.ShouldBe("CN-A");
        outcome.Exclusions.ShouldContain(e => e.Code == "TRANSPORTER_MISMATCH");
    }

    [Fact]
    public void Two_contracts_of_one_transporter_with_equally_good_rates_are_a_conflict()
    {
        var t = Guid.NewGuid();
        var first = ActiveWith([MumbaiPune(40_000m)], transporter: t, number: "CN-A");
        var second = ActiveWith([MumbaiPune(38_000m)], transporter: t, number: "CN-B");

        var outcome = Run([first, second]);

        outcome.ErrorCode.ShouldBe("FREIGHT_RATE_CONFLICT");
        outcome.Exclusions.Count(e => e.Code == "CONTRACT_CONFLICT").ShouldBe(2);
    }

    [Fact]
    public void A_missing_quantity_is_reported_as_missing_not_as_no_rate()
    {
        var contract = ActiveWith([Rate(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), new SlabRatePricing(ContractType.Ptl, SlabDimension.Weight, RateUnit.Kg, SlabMethod.Flat, [new Slab(0, null, 9)]))], ContractType.Ptl);

        var outcome = Run([contract], Input(service: ContractType.Ptl));

        outcome.ErrorCode.ShouldBe("FREIGHT_INPUT_MISSING");
        outcome.Message!.ShouldContain("weight");
    }

    [Fact]
    public void Dph_follows_the_rule_version_in_force_and_the_period_price()
    {
        var contract = ActiveWith([MumbaiPune(38_000m)], dph: [Dph(basePrice: 90m, frequency: DphFrequency.Monthly)]);
        var prices = new[] { Diesel(90m, new DateOnly(2026, 6, 1)), Diesel(99m, new DateOnly(2026, 6, 10)) };

        // 15 June is in the month starting 1 June, whose price is 90: no change, even though 99 applies from the 10th
        Run([contract], Input(vehicle: Truck32, km: 155m, date: new DateOnly(2026, 6, 15)), Context(prices: prices)).Selected!.Dph.ShouldBe(0m);
        // in July the month's price is the 99 that is then in force
        Run([contract], Input(vehicle: Truck32, km: 155m, date: new DateOnly(2026, 7, 15)), Context(prices: prices)).Selected!.Dph.ShouldBe(1_140m);
    }

    [Fact]
    public void With_no_diesel_price_there_is_no_adjustment_and_the_notes_say_so()
    {
        var contract = ActiveWith([MumbaiPune(38_000m)], dph: [Dph()]);

        var best = Run([contract], context: Context()).Selected!;

        best.Dph.ShouldBe(0m);
        best.Notes.ShouldContain(n => n.Contains("No diesel price on file"));
    }

    [Fact]
    public void A_diesel_snapshot_is_used_in_preference_to_the_live_index_so_a_past_rating_is_reproducible()
    {
        var contract = ActiveWith([MumbaiPune(38_000m)], dph: [Dph()]);
        var rule = contract.DphRules.Single();
        var snapshot = DphPeriodSnapshot.Create(Tenant, rule, Today, 99m, 10m, 3m, "1.0");
        var snapshots = new Dictionary<(Guid, DateOnly), DphPeriodSnapshot> { [(rule.Id, Today)] = snapshot };

        var live = Context(prices: [Diesel(120m, Today.AddDays(-1))]);
        var withSnapshot = live with { Snapshots = snapshots };

        Run([contract], context: live).Selected!.Dph.ShouldBe(38_000m * 0.10m);   // 33% x 30% = 10%
        Run([contract], context: withSnapshot).Selected!.Dph.ShouldBe(1_140m);     // the recorded 99 -> 3%
    }

    [Fact]
    public void The_older_diesel_clause_still_adjusts_freight_when_a_contract_has_no_dph_rules()
    {
        var contract = Draft(fuel: Fuel(basePrice: 90m, step: 1m, impact: 0.5m));
        contract.ReplaceRates([MumbaiPune(38_000m)]);
        contract.MarkSubmitted(Guid.NewGuid(), Tms.SharedKernel.Contracts.ApprovalStatus.Approved, Now);

        var best = Run([contract], context: Context(prices: [Diesel(94m, Today.AddDays(-3))])).Selected!;

        best.Lines.ShouldContain(l => l.Type == "DPH");
        best.Dph.ShouldBe(760m); // 4 steps x 0.5% of 38,000
    }

    [Fact]
    public void A_discount_applies_to_freight_and_dph_and_rounding_makes_the_total_a_clean_amount()
    {
        var terms = new ContractTerms(250m, 24m, 100m, 0m, 0m, 0m, 0m, null, new RoundingRule(RoundingMode.Nearest, 10m, 0), 2m);
        var contract = ActiveWith([MumbaiPune(38_000m)], dph: [Dph()], terms: terms);

        var best = Run([contract], context: Context(prices: [Diesel(99m, Today.AddDays(-1))])).Selected!;

        best.Discount.ShouldBe(-(39_140m * 0.02m)); // 2% of 38,000 + 1,140
        best.Total.ShouldBe(38_360m);
        best.Lines.Sum(l => l.Amount).ShouldBe(best.Total);
        (best.Total % 10).ShouldBe(0m);
        best.Lines.ShouldContain(l => l.Type == "ROUNDING");
    }

    [Fact]
    public void Terms_based_charges_are_still_applied_and_a_contract_charge_with_the_same_code_replaces_them()
    {
        var terms = new ContractTerms(250m, 24m, 100m, 800m, 0m, 0m, 0m, null);
        var legacy = ActiveWith([MumbaiPune(38_000m)], terms: terms);
        var replaced = ActiveWith([MumbaiPune(38_000m)], terms: terms, accessorials: [new AccessorialSpec("LOADING", "Loading", AccessorialCalc.Fixed, "TRIP", 1_000m, AutoApply: true)]);

        Run([legacy]).Selected!.Accessorials.ShouldBe(800m);
        Run([replaced]).Selected!.Accessorials.ShouldBe(1_000m); // not 1,800
    }

    [Fact]
    public void The_detention_example_is_worked_out_automatically_from_actual_hours()
    {
        var detention = new AccessorialSpec("DETENTION", "Detention", AccessorialCalc.Tiered, "HOUR", Tiers: [new AccessorialTier(0, 2, 0), new AccessorialTier(2, 5, 500), new AccessorialTier(5, null, 750)]);
        var contract = ActiveWith([MumbaiPune(38_000m)], accessorials: [detention]);

        Run([contract], Input(vehicle: Truck32, km: 155m, inputs: new() { ["DETENTION"] = 6m })).Selected!.Accessorials.ShouldBe(2_250m);
        Run([contract]).Selected!.Accessorials.ShouldBe(0m);
    }

    [Fact]
    public void An_extra_stop_is_worked_out_from_the_stop_count()
    {
        var stop = new AccessorialSpec("ADDITIONAL_STOP", "Additional stop", AccessorialCalc.PerUnit, "STOP", 750m, IncludedQuantity: 2);
        var contract = ActiveWith([MumbaiPune(38_000m)], accessorials: [stop]);

        Run([contract], Input(vehicle: Truck32, km: 155m, stops: 3)).Selected!.Accessorials.ShouldBe(750m);
        Run([contract], Input(vehicle: Truck32, km: 155m, stops: 2)).Selected!.Accessorials.ShouldBe(0m);
    }

    [Fact]
    public void A_dedicated_vehicle_is_rated_at_its_monthly_rental_with_excess_when_usage_is_given()
    {
        var pricing = new DedicatedPricing(185_000m, 6_000m, 32m, 0m, 0m);
        var contract = ActiveWith([Rate(Place.Anywhere, Place.Anywhere, pricing, Truck32, minKm: 0, maxKm: 100_000)], ContractType.Dedicated);

        var rental = Run([contract], Input(service: ContractType.Dedicated, vehicle: Truck32, km: 100m));
        var excess = Run([contract], Input(service: ContractType.Dedicated, vehicle: Truck32, km: 100m, inputs: new() { ["KM_RUN"] = 6_500m, ["HOURS_RUN"] = 0m }));

        rental.Selected!.Total.ShouldBe(185_000m);
        rental.Selected.Notes.ShouldContain(n => n.Contains("billed monthly"));
        excess.Selected!.Total.ShouldBe(185_000m + (500m * 32m));
    }

    [Fact]
    public void A_contract_can_cover_several_services_and_each_is_rated_by_its_own_rates()
    {
        var contract = ActiveWith(
        [
            Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), Truck32),
            Rate(MumbaiCity, PuneCity, new SlabRatePricing(ContractType.Ptl, SlabDimension.Weight, RateUnit.Kg, SlabMethod.Flat, [new Slab(0, null, 9)])),
        ], ContractType.Ftl, services: [ContractType.Ptl]);

        Run([contract], Input(vehicle: Truck32, km: 155m)).Selected!.BaseFreight.ShouldBe(38_000m);
        Run([contract], Input(service: ContractType.Ptl, kg: 1_000m)).Selected!.BaseFreight.ShouldBe(9_000m);
        Run([contract], Input(service: ContractType.Dedicated, vehicle: Truck32)).Exclusions.ShouldContain(e => e.Code == "SERVICE_NOT_COVERED");
    }

    [Fact]
    public void The_result_does_not_depend_on_the_order_the_contracts_arrive_in()
    {
        var contracts = Enumerable.Range(1, 6).Select(i => ActiveWith([MumbaiPune(38_000m + (i * 500))], transporter: Guid.NewGuid(), number: $"CN-{i}")).ToList();
        var forward = Run(contracts);
        var reversed = Run(Enumerable.Reverse(contracts));
        var shuffled = Run(contracts.OrderBy(c => c.Id).ToList());

        reversed.Options.Select(o => o.Contract.Number).ShouldBe(forward.Options.Select(o => o.Contract.Number));
        shuffled.Options.Select(o => o.Contract.Number).ShouldBe(forward.Options.Select(o => o.Contract.Number));
        forward.Trace.Select(t => t.Text).ShouldBe(reversed.Trace.Select(t => t.Text));
    }

    [Fact]
    public void A_calculation_made_today_is_the_same_next_year_because_the_inputs_decide_it()
    {
        var contract = ActiveWith([MumbaiPune(38_000m)], dph: [Dph()]);
        var ctx = Context(prices: [Diesel(102m, Today.AddDays(-5))]);

        var first = Run([contract], context: ctx).Selected!;
        var again = Run([contract], context: ctx).Selected!;

        again.Total.ShouldBe(first.Total);
        again.Lines.Select(l => (l.Type, l.Amount)).ShouldBe(first.Lines.Select(l => (l.Type, l.Amount)));
        RatingEngine.Version.ShouldBe("1.0");
    }
}
