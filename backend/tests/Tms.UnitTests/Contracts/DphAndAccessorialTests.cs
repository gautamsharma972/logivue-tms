using Tms.Modules.Contracts.Domain;
using static Tms.UnitTests.Contracts.ContractTestData;
using static Tms.UnitTests.Contracts.RatingTestData;

namespace Tms.UnitTests.Contracts;

public class DphCalculatorTests
{
    private static readonly DateOnly Ref = new(2026, 10, 7);

    private static DphResult Run(DphRuleSpec rule, decimal price, decimal baseFreight = 38_000m, decimal? km = 155m) => DphCalculator.Calculate(rule, price, Ref, baseFreight, km);

    [Fact]
    public void A_diesel_rise_escalates_freight_by_the_variation_times_the_fuel_share()
    {
        var result = Run(Dph(), 99m); // +10% x 30% = 3%

        result.VariationPercent.ShouldBe(10m);
        result.AdjustmentPercent.ShouldBe(3m);
        result.Amount.ShouldBe(1_140m);
        result.Applied.ShouldBeTrue();
        result.Explanation.ShouldContain("3");
    }

    [Fact]
    public void A_diesel_fall_de_escalates_freight()
    {
        var result = Run(Dph(), 81m);

        result.AdjustmentPercent.ShouldBe(-3m);
        result.Amount.ShouldBe(-1_140m);
    }

    [Fact]
    public void The_contract_example_of_a_fuel_share_of_thirty_percent_gives_forty_one_thousand_three_hundred_and_twenty_with_toll()
    {
        // 38,000 base; diesel 90 -> 102 is +13.33%; x 30% = 4% = 1,520
        var result = Run(Dph(), 102m);

        result.AdjustmentPercent.ShouldBe(4m);
        result.Amount.ShouldBe(1_520m);
    }

    [Theory]
    [InlineData(93.0, false)]   // +3.3%: inside +-5%
    [InlineData(94.5, false)]   // exactly +5% is still inside
    [InlineData(95.0, true)]
    [InlineData(85.5, false)]   // exactly -5%
    [InlineData(85.0, true)]
    public void A_threshold_is_a_dead_band_around_the_base_price(double price, bool applies) =>
        Run(Dph(threshold: 5m), (decimal)price).Applied.ShouldBe(applies);

    [Fact]
    public void Past_the_dead_band_the_whole_variation_counts_unless_the_rule_says_only_the_excess_does()
    {
        Run(Dph(threshold: 5m), 99m).AdjustmentPercent.ShouldBe(3m);                       // 10% x 30%
        Run(Dph(threshold: 5m, onExcess: true), 99m).AdjustmentPercent.ShouldBe(1.5m);     // (10-5)% x 30%
    }

    [Fact]
    public void A_rule_that_only_escalates_ignores_a_fall_and_the_reverse()
    {
        Run(Dph(direction: DphDirection.EscalationOnly), 81m).Applied.ShouldBeFalse();
        Run(Dph(direction: DphDirection.EscalationOnly), 99m).Applied.ShouldBeTrue();
        Run(Dph(direction: DphDirection.DeEscalationOnly), 99m).Applied.ShouldBeFalse();
        Run(Dph(direction: DphDirection.DeEscalationOnly), 81m).Applied.ShouldBeTrue();
    }

    [Fact]
    public void Threshold_steps_move_freight_by_a_set_percentage_per_whole_step_up_to_a_cap()
    {
        var rule = Dph(formula: DphFormula.ThresholdSteps, step: 2m, impact: 1m);

        Run(rule, 99m).AdjustmentPercent.ShouldBe(5m);   // +10% = 5 steps of 2%
        Run(rule, 91.5m).Applied.ShouldBeFalse();        // +1.67%: not a whole step
        Run(rule with { CapPercent = 3m }, 99m).AdjustmentPercent.ShouldBe(3m);
    }

    [Fact]
    public void A_fixed_adjustment_adds_rupees_per_step()
    {
        var result = Run(Dph(formula: DphFormula.FixedAdjustment, step: 5m, fixedPerStep: 500m), 99m);

        result.Steps.ShouldBe(2);
        result.Amount.ShouldBe(1_000m);
        Run(Dph(formula: DphFormula.FixedAdjustment, step: 5m, fixedPerStep: 500m), 81m).Amount.ShouldBe(-1_000m);
    }

    [Fact]
    public void A_per_km_adjustment_needs_the_distance()
    {
        var rule = Dph(formula: DphFormula.PerKmAdjustment, step: 5m, perKm: 0.5m);

        Run(rule, 99m, km: 150m).Amount.ShouldBe(150m); // 2 steps x 0.5 x 150
        Run(rule, 99m, km: null).Applied.ShouldBeFalse();
    }

    [Fact]
    public void An_indexed_rule_follows_the_index_in_proportion_with_no_threshold()
    {
        var result = Run(Dph(formula: DphFormula.Indexed, threshold: 50m), 91m);

        result.Applied.ShouldBeTrue();
        result.AdjustmentPercent.ShouldBe(0.33m);
    }

    [Fact]
    public void The_reference_day_depends_on_how_often_the_rule_reprices()
    {
        DphCalculator.ReferenceDate(DphFrequency.Shipment, new DateOnly(2026, 7, 15)).ShouldBe(new DateOnly(2026, 7, 15));
        DphCalculator.ReferenceDate(DphFrequency.Monthly, new DateOnly(2026, 7, 15)).ShouldBe(new DateOnly(2026, 7, 1));
        DphCalculator.ReferenceDate(DphFrequency.Quarterly, new DateOnly(2026, 8, 20)).ShouldBe(new DateOnly(2026, 7, 1));
        DphCalculator.ReferenceDate(DphFrequency.Weekly, new DateOnly(2026, 10, 7)).ShouldBe(new DateOnly(2026, 10, 5)); // the Monday
        DphCalculator.ReferenceDate(DphFrequency.Fortnightly, new DateOnly(2026, 10, 20)).ShouldBe(new DateOnly(2026, 10, 16));
        DphCalculator.ReferenceDate(DphFrequency.Fortnightly, new DateOnly(2026, 10, 9)).ShouldBe(new DateOnly(2026, 10, 1));
    }

    [Theory]
    [InlineData("percent without fuel share")]
    [InlineData("steps without impact")]
    [InlineData("fixed without amount")]
    [InlineData("base price missing")]
    public void Rules_missing_what_their_formula_needs_are_refused(string problem)
    {
        var rule = problem switch
        {
            "percent without fuel share" => Dph(fuel: 0m),
            "steps without impact" => Dph(formula: DphFormula.ThresholdSteps, step: 2m, impact: 0m),
            "fixed without amount" => Dph(formula: DphFormula.FixedAdjustment, step: 5m),
            _ => Dph(basePrice: 0m),
        };

        rule.Validate().IsFailure.ShouldBeTrue();
    }
}

public class DphVersionTests
{
    [Fact]
    public void Versions_of_one_rule_are_numbered_by_start_date_and_the_one_in_force_on_a_date_is_found()
    {
        var contract = Draft();
        contract.ReplaceDphRules(
        [
            Dph(from: new DateOnly(2026, 7, 1), to: new DateOnly(2026, 9, 30), basePrice: 92m),
            Dph(from: new DateOnly(2026, 4, 1), to: new DateOnly(2026, 6, 30), basePrice: 90m),
            Dph(from: new DateOnly(2026, 10, 1), to: new DateOnly(2026, 12, 31), basePrice: 95m),
        ]).IsSuccess.ShouldBeTrue();

        contract.DphRules.Select(r => (r.Version, r.Spec.BaseDieselPrice)).OrderBy(x => x.Version).ShouldBe([(1, 90m), (2, 92m), (3, 95m)]);
        var rules = contract.DphRules.ToList();
        DphCalculator.InForce(rules, null, new DateOnly(2026, 5, 10), contract.EffectiveFrom, contract.EffectiveTo)!.Version.ShouldBe(1);
        DphCalculator.InForce(rules, "dph", new DateOnly(2026, 8, 10), contract.EffectiveFrom, contract.EffectiveTo)!.Version.ShouldBe(2);
        DphCalculator.InForce(rules, null, new DateOnly(2026, 10, 7), contract.EffectiveFrom, contract.EffectiveTo)!.Version.ShouldBe(3);
    }

    [Fact]
    public void Versions_that_overlap_in_time_are_refused()
    {
        var contract = Draft();

        var result = contract.ReplaceDphRules([Dph(from: new DateOnly(2026, 4, 1), to: new DateOnly(2026, 6, 30)), Dph(from: new DateOnly(2026, 6, 15), to: new DateOnly(2026, 9, 30))]);

        result.IsFailure.ShouldBeTrue();
        result.Error.Description.ShouldContain("overlap");
    }

    [Fact]
    public void Two_different_rules_cannot_both_be_the_default_on_the_same_day()
    {
        var contract = Draft();

        contract.ReplaceDphRules([Dph("A"), Dph("B")]).Error.Description.ShouldContain("default");
        contract.ReplaceDphRules([Dph("A"), Dph("B", isDefault: false)]).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void A_rate_cannot_name_a_dph_rule_the_contract_does_not_have()
    {
        var contract = Draft();

        contract.ReplaceRates([MumbaiPune(38_000m, new RateExtras(DphRuleCode: "MISSING"))]).Error.Description.ShouldContain("DPH rule MISSING");
        contract.ReplaceDphRules([Dph("OK")]).IsSuccess.ShouldBeTrue();
        contract.ReplaceRates([MumbaiPune(38_000m, new RateExtras(DphRuleCode: "ok"))]).IsSuccess.ShouldBeTrue();
        contract.ReplaceDphRules([Dph("OTHER")]).Error.Description.ShouldContain("OK");
    }

    [Fact]
    public void An_unlisted_code_finds_only_the_default_rule()
    {
        var contract = Draft();
        contract.ReplaceDphRules([Dph("STD"), Dph("SPECIAL", isDefault: false)]).IsSuccess.ShouldBeTrue();
        var rules = contract.DphRules.ToList();

        DphCalculator.InForce(rules, null, Today, contract.EffectiveFrom, contract.EffectiveTo)!.Code.ShouldBe("STD");
        DphCalculator.InForce(rules, "SPECIAL", Today, contract.EffectiveFrom, contract.EffectiveTo)!.Code.ShouldBe("SPECIAL");
        DphCalculator.InForce(rules, "NOPE", Today, contract.EffectiveFrom, contract.EffectiveTo).ShouldBeNull();
    }
}

public class AccessorialCalculatorTests
{
    private static AccessorialContext Context(int stops = 1, decimal kg = 12_500m, ContractType service = ContractType.Ftl, Dictionary<string, decimal>? inputs = null, string[]? caps = null, DateOnly? date = null, decimal baseFreight = 38_000m) =>
        new(date ?? Today, service, kg, stops, (caps ?? []).ToHashSet(), inputs ?? [], baseFreight);

    private static readonly AccessorialSpec Detention = new("DETENTION", "Detention", AccessorialCalc.Tiered, "HOUR", Tiers: [new AccessorialTier(0, 2, 0), new AccessorialTier(2, 5, 500), new AccessorialTier(5, null, 750)]);

    [Theory]
    [InlineData(1.5, null)]     // inside the first two free hours
    [InlineData(2, null)]
    [InlineData(4, 1_000.0)]      // hours 3-4 at 500
    [InlineData(5, 1_500.0)]
    [InlineData(6, 2_250.0)]      // hours 3-5 at 500, hour 6 at 750
    public void Detention_tiers_charge_nothing_for_included_hours_and_each_band_at_its_own_rate(double hours, double? expected)
    {
        var line = AccessorialCalculator.Calculate(Detention, Context(inputs: new() { ["DETENTION"] = (decimal)hours }));

        if (expected is null)
        {
            line.ShouldBeNull();
        }
        else
        {
            line!.Amount.ShouldBe((decimal)expected);
        }
    }

    [Fact]
    public void Nothing_is_charged_for_what_did_not_happen() =>
        AccessorialCalculator.Calculate(Detention, Context()).ShouldBeNull();

    [Fact]
    public void An_extra_stop_is_charged_beyond_the_stops_included()
    {
        var extraStop = new AccessorialSpec("ADDITIONAL_STOP", "Additional stop", AccessorialCalc.PerUnit, "STOP", 750m, IncludedQuantity: 2);

        AccessorialCalculator.Calculate(extraStop, Context(stops: 2)).ShouldBeNull();
        AccessorialCalculator.Calculate(extraStop, Context(stops: 3))!.Amount.ShouldBe(750m);
        AccessorialCalculator.Calculate(extraStop, Context(stops: 5))!.Amount.ShouldBe(2_250m);
    }

    [Fact]
    public void A_reimbursed_cost_is_passed_through_within_the_contract_limits()
    {
        var toll = new AccessorialSpec("TOLL", "Toll", AccessorialCalc.Reimbursed, "TRIP", MinimumCharge: 500m, MaximumCharge: 1_500m);

        AccessorialCalculator.Calculate(toll, Context(inputs: new() { ["TOLL"] = 1_200m }))!.Amount.ShouldBe(1_200m);
        AccessorialCalculator.Calculate(toll, Context(inputs: new() { ["TOLL"] = 1_800m }))!.Amount.ShouldBe(1_500m);
        AccessorialCalculator.Calculate(toll, Context(inputs: new() { ["TOLL"] = 200m }))!.Amount.ShouldBe(500m);
    }

    [Fact]
    public void Extra_kilometres_and_minimum_and_maximum_charges_apply()
    {
        var extraKm = new AccessorialSpec("EXTRA_KM", "Extra km", AccessorialCalc.PerUnit, "KM", 25m, MinimumCharge: 300m, MaximumCharge: 2_000m);

        AccessorialCalculator.Calculate(extraKm, Context(inputs: new() { ["EXTRA_KM"] = 4m }))!.Amount.ShouldBe(300m);
        AccessorialCalculator.Calculate(extraKm, Context(inputs: new() { ["EXTRA_KM"] = 40m }))!.Amount.ShouldBe(1_000m);
        AccessorialCalculator.Calculate(extraKm, Context(inputs: new() { ["EXTRA_KM"] = 400m }))!.Amount.ShouldBe(2_000m);
    }

    [Fact]
    public void A_charge_applies_only_when_its_trigger_does()
    {
        var heavy = new AccessorialSpec("HANDLING", "Handling", AccessorialCalc.Fixed, "TRIP", 900m, AutoApply: true,
            Trigger: new AccessorialTrigger([ContractType.Ptl], MinWeightKg: 1_000m, RequiredCapabilities: ["HAZMAT"]));

        AccessorialCalculator.Calculate(heavy, Context(service: ContractType.Ftl, caps: ["HAZMAT"])).ShouldBeNull();
        AccessorialCalculator.Calculate(heavy, Context(service: ContractType.Ptl, kg: 500, caps: ["HAZMAT"])).ShouldBeNull();
        AccessorialCalculator.Calculate(heavy, Context(service: ContractType.Ptl, kg: 2_000)).ShouldBeNull();
        AccessorialCalculator.Calculate(heavy, Context(service: ContractType.Ptl, kg: 2_000, caps: ["HAZMAT"]))!.Amount.ShouldBe(900m);
    }

    [Fact]
    public void A_charge_has_its_own_effective_dates()
    {
        var spec = new AccessorialSpec("LOADING", "Loading", AccessorialCalc.Fixed, "TRIP", 500m, ValidFrom: new DateOnly(2026, 7, 1), ValidTo: new DateOnly(2026, 9, 30), AutoApply: true);

        AccessorialCalculator.Calculate(spec, Context(date: new DateOnly(2026, 6, 30))).ShouldBeNull();
        AccessorialCalculator.Calculate(spec, Context(date: new DateOnly(2026, 8, 1)))!.Amount.ShouldBe(500m);
        AccessorialCalculator.Calculate(spec, Context(date: new DateOnly(2026, 10, 1))).ShouldBeNull();
    }

    [Fact]
    public void A_percentage_of_freight_is_worked_from_the_base_freight()
    {
        var oda = new AccessorialSpec("ODA", "Out of delivery area", AccessorialCalc.PercentOfFreight, "TRIP", 5m, AutoApply: true);

        AccessorialCalculator.Calculate(oda, Context(baseFreight: 20_000m))!.Amount.ShouldBe(1_000m);
    }

    [Theory]
    [InlineData("tiers gap")]
    [InlineData("tiers not from zero")]
    [InlineData("rate missing")]
    [InlineData("max below min")]
    public void Charges_that_cannot_be_worked_out_are_refused(string problem)
    {
        var spec = problem switch
        {
            "tiers gap" => new AccessorialSpec("X", "X", AccessorialCalc.Tiered, "HOUR", Tiers: [new AccessorialTier(0, 2, 0), new AccessorialTier(3, null, 500)]),
            "tiers not from zero" => new AccessorialSpec("X", "X", AccessorialCalc.Tiered, "HOUR", Tiers: [new AccessorialTier(1, null, 500)]),
            "rate missing" => new AccessorialSpec("X", "X", AccessorialCalc.PerUnit, "KM"),
            _ => new AccessorialSpec("X", "X", AccessorialCalc.Fixed, "TRIP", 100m, MinimumCharge: 500m, MaximumCharge: 400m),
        };

        spec.Validate().IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void The_same_charge_cannot_be_defined_twice_for_the_same_dates()
    {
        var contract = Draft();
        var loading = new AccessorialSpec("LOADING", "Loading", AccessorialCalc.Fixed, "TRIP", 500m);

        contract.ReplaceAccessorials([loading, loading with { Rate = 600m }]).IsFailure.ShouldBeTrue();
        contract.ReplaceAccessorials([loading with { ValidTo = Today }, loading with { ValidFrom = Today.AddDays(1), Rate = 600m }]).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void The_standard_catalogue_covers_the_charges_a_contract_needs()
    {
        var codes = AccessorialType.Standard.Select(x => x.Code).ToList();

        codes.ShouldContain("DETENTION");
        codes.ShouldContain("TOLL");
        codes.ShouldContain("NIGHT_HALT");
        codes.ShouldContain("ODA");
        codes.Count.ShouldBe(codes.Distinct().Count());
    }
}
