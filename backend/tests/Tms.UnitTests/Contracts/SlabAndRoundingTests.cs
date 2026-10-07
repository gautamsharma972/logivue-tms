using Tms.Modules.Contracts.Domain;
using static Tms.UnitTests.Contracts.ContractTestData;
using static Tms.UnitTests.Contracts.RatingTestData;

namespace Tms.UnitTests.Contracts;

public class SlabPricingTests
{
    private static SlabRatePricing Weight(SlabMethod method, RateUnit unit = RateUnit.Kg, params Slab[] slabs) =>
        new(ContractType.Ptl, SlabDimension.Weight, unit, method, slabs.Length > 0 ? slabs : [new Slab(0, 500, 10), new Slab(500, 1000, 9), new Slab(1000, 3000, 8), new Slab(3000, null, 7)]);

    [Theory]
    [InlineData(400, 4000)]    // 0-500 at 10
    [InlineData(500, 5000)]    // a slab includes its upper limit
    [InlineData(501, 4509)]    // 500-1000 at 9, applied to the whole quantity
    [InlineData(2000, 16000)]
    [InlineData(5000, 35000)]
    public void A_flat_slab_prices_the_whole_quantity_at_the_rate_of_the_slab_it_falls_into(decimal kg, decimal expected) =>
        Weight(SlabMethod.Flat).Price(kg).Amount.ShouldBe(expected);

    [Fact]
    public void A_progressive_table_adds_up_the_slices_it_reaches()
    {
        // 3500 kg: 500x10 + 500x9 + 2000x8 + 500x7
        Weight(SlabMethod.Progressive).Price(3500).Amount.ShouldBe(5000m + 4500m + 16000m + 3500m);
    }

    [Fact]
    public void Base_plus_excess_is_a_fixed_price_for_the_first_slab_then_the_excess_by_slab()
    {
        var slabs = new SlabRatePricing(ContractType.Ftl, SlabDimension.Distance, RateUnit.Km, SlabMethod.BaseExcess, [new Slab(0, 200, 38_000, SlabRateType.Fixed), new Slab(200, 500, 90), new Slab(500, null, 70)]);

        slabs.Price(150).Amount.ShouldBe(38_000m);
        slabs.Price(200).Amount.ShouldBe(38_000m);
        slabs.Price(250).Amount.ShouldBe(38_000m + (50m * 90m));
        slabs.Price(600).Amount.ShouldBe(38_000m + (300m * 90m) + (100m * 70m));
    }

    [Fact]
    public void A_fixed_slab_is_one_price_for_the_band_in_a_flat_table()
    {
        var slabs = new SlabRatePricing(ContractType.Ftl, SlabDimension.Weight, RateUnit.Trip, SlabMethod.Flat, [new Slab(0, 10_000, 30_000, SlabRateType.Fixed), new Slab(10_000, 15_000, 38_000, SlabRateType.Fixed)]);

        slabs.Price(12_500).Amount.ShouldBe(38_000m);
        slabs.Price(9_000).Amount.ShouldBe(30_000m);
    }

    [Fact]
    public void A_rate_per_ton_converts_kilograms()
    {
        var perTon = Weight(SlabMethod.Flat, RateUnit.Ton, new Slab(0, null, 4_000));

        perTon.Price(2_500).Amount.ShouldBe(10_000m); // 2.5 t x 4,000
    }

    [Fact]
    public void Volume_and_package_slabs_work_like_weight_slabs()
    {
        var cbm = new SlabRatePricing(ContractType.Ptl, SlabDimension.Volume, RateUnit.Cbm, SlabMethod.Flat, [new Slab(0, 10, 900), new Slab(10, null, 800)]);
        var boxes = new SlabRatePricing(ContractType.Ptl, SlabDimension.Packages, RateUnit.Box, SlabMethod.Progressive, [new Slab(0, 50, 20), new Slab(50, null, 15)]);

        cbm.Price(8).Amount.ShouldBe(7_200m);
        cbm.Price(12).Amount.ShouldBe(9_600m);
        boxes.Price(80).Amount.ShouldBe((50m * 20m) + (30m * 15m));
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("first")]
    [InlineData("openMiddle")]
    [InlineData("zeroRate")]
    [InlineData("baseExcessShape")]
    public void Badly_shaped_tables_are_refused(string problem)
    {
        Pricing pricing = problem switch
        {
            "gap" => Weight(SlabMethod.Flat, RateUnit.Kg, new Slab(0, 100, 10), new Slab(150, null, 9)),
            "first" => Weight(SlabMethod.Flat, RateUnit.Kg, new Slab(50, null, 10)),
            "openMiddle" => Weight(SlabMethod.Flat, RateUnit.Kg, new Slab(0, null, 10), new Slab(100, null, 9)),
            "zeroRate" => Weight(SlabMethod.Flat, RateUnit.Kg, new Slab(0, null, 0)),
            _ => new SlabRatePricing(ContractType.Ftl, SlabDimension.Distance, RateUnit.Km, SlabMethod.BaseExcess, [new Slab(0, 100, 90), new Slab(100, null, 70)]),
        };

        pricing.Validate().IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_unit_that_does_not_fit_the_dimension_is_refused() =>
        new SlabRatePricing(ContractType.Ptl, SlabDimension.Weight, RateUnit.Km, SlabMethod.Flat, [new Slab(0, null, 5)]).Validate().IsFailure.ShouldBeTrue();

    [Fact]
    public void A_slab_rate_is_priced_through_the_same_base_freight_step_as_every_other_pricing()
    {
        var pricing = Weight(SlabMethod.Flat);
        var result = FreightCalculator.BaseFreight(Terms(), pricing, Query(weight: 800m, type: ContractType.Ptl));

        result.IsSuccess.ShouldBeTrue();
        result.Value.Amount.ShouldBe(7_200m);
        result.Value.Basis.ShouldContain("800");
    }

    [Fact]
    public void Volumetric_weight_is_charged_when_it_is_heavier_than_the_actual_weight()
    {
        var result = FreightCalculator.BaseFreight(Terms(volumetric: 250m), Weight(SlabMethod.Flat), Query(weight: 400m, volume: 4m, type: ContractType.Ptl));

        result.Value.ChargeableWeightKg.ShouldBe(1_000m); // 4 CBM x 250
        result.Value.Amount.ShouldBe(9_000m);              // the 500-1000 slab at 9 applies to 1,000 kg
    }

    [Fact]
    public void A_missing_quantity_is_asked_for_not_assumed()
    {
        FreightCalculator.BaseFreight(Terms(), Weight(SlabMethod.Flat), Query(type: ContractType.Ptl)).Error.Code.ShouldBe("quote.weight_required");
        FreightCalculator.BaseFreight(Terms(), new SlabRatePricing(ContractType.Ftl, SlabDimension.Distance, RateUnit.Km, SlabMethod.Flat, [new Slab(0, null, 40)]), Query()).Error.Code.ShouldBe("quote.distance_required");
    }
}

public class RoundingAndLimitsTests
{
    [Theory]
    [InlineData(RoundingMode.Nearest, 10, 41_237.26, 41_240)]
    [InlineData(RoundingMode.Up, 10, 41_231.00, 41_240)]
    [InlineData(RoundingMode.Down, 10, 41_239.99, 41_230)]
    [InlineData(RoundingMode.Nearest, 0.5, 100.74, 100.5)]
    [InlineData(RoundingMode.Nearest, 0.01, 100.456, 100.46)]
    public void Totals_round_to_the_increment_in_the_direction_given(RoundingMode mode, double increment, double amount, double expected) =>
        new RoundingRule(mode, (decimal)increment).Apply((decimal)amount).ShouldBe((decimal)expected);

    [Fact]
    public void Rounding_rules_are_validated()
    {
        new RoundingRule(RoundingMode.Nearest, 0).Validate().IsFailure.ShouldBeTrue();
        new RoundingRule(RoundingMode.Nearest, 10, 9).Validate().IsFailure.ShouldBeTrue();
        RoundingRule.Paise.Validate().IsSuccess.ShouldBeTrue();
    }

    private static Contract Contract_(RateExtras extras, decimal amount) => ActiveWith([MumbaiPune(amount, extras)]);

    [Fact]
    public void A_minimum_charge_raises_a_low_calculated_freight()
    {
        var card = Contract_(new RateExtras(MinimumCharge: 1_500m), 1_100m).RateCards.Single();

        var (amount, basis) = FreightCalculator.ApplyCardLimits(card, 1_100m, "x");

        amount.ShouldBe(1_500m);
        basis.ShouldContain("minimum");
    }

    [Fact]
    public void A_maximum_charge_caps_a_high_calculated_freight_and_only_when_one_is_set()
    {
        var capped = Contract_(new RateExtras(MaximumCharge: 48_000m), 52_000m).RateCards.Single();
        var open = Contract_(new RateExtras(), 52_000m).RateCards.Single();

        FreightCalculator.ApplyCardLimits(capped, 52_000m, "x").Amount.ShouldBe(48_000m);
        FreightCalculator.ApplyCardLimits(open, 52_000m, "x").Amount.ShouldBe(52_000m);
    }

    [Fact]
    public void A_maximum_below_the_minimum_is_refused()
    {
        var contract = Draft();
        contract.ReplaceRates([MumbaiPune(100m, new RateExtras(MinimumCharge: 500m, MaximumCharge: 400m))]).IsFailure.ShouldBeTrue();
    }
}
