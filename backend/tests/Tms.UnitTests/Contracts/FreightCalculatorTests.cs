using Tms.Modules.Contracts.Domain;
using static Tms.UnitTests.Contracts.ContractTestData;

namespace Tms.UnitTests.Contracts;

public class FreightCalculatorTests
{
    private static (Contract Contract, RateCard Card) PtlRate(Pricing pricing, FuelClause? fuel = null, ContractTerms? terms = null)
    {
        var contract = Active(ContractType.Ptl, [Spec(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), pricing)], fuel, terms);
        return (contract, contract.RateCards.Single());
    }

    private static (Contract Contract, RateCard Card) FtlRate(Pricing pricing, FuelClause? fuel = null, ContractTerms? terms = null)
    {
        var contract = Active(ContractType.Ftl, [Spec(Place.OfCity("Maharashtra", "Pune"), Place.OfCity("Maharashtra", "Mumbai"), pricing, Truck32)], fuel, terms);
        return (contract, contract.RateCards.Single());
    }

    private static decimal Total(Contract c, RateCard r, FreightQuery q, decimal? diesel = null) => FreightCalculator.Calculate(c, r, q, diesel).Value.Total;

    // ---------------- FTL ----------------

    [Fact]
    public void A_flat_trip_rate_is_the_trip_price()
    {
        var (c, r) = FtlRate(new FlatTripPricing(42_000m));

        var quote = FreightCalculator.Calculate(c, r, Query(vehicle: Truck32), null).Value;

        quote.Total.ShouldBe(42_000m);
        quote.Lines.ShouldHaveSingleItem().Code.ShouldBe("FREIGHT");
        quote.Lane.ShouldBe("PUNE, MAHARASHTRA → MUMBAI, MAHARASHTRA");
    }

    [Fact]
    public void A_per_km_rate_bills_the_distance_with_a_minimum_km_and_minimum_charge()
    {
        var (c, r) = FtlRate(new PerKmPricing(RatePerKm: 38m, MinKm: 200m, MinCharge: 9_000m));

        Total(c, r, Query(vehicle: Truck32, km: 450m)).ShouldBe(17_100m);
        Total(c, r, Query(vehicle: Truck32, km: 120m)).ShouldBe(9_000m, "120 km is billed as 200 km = 7,600, below the 9,000 minimum");
        FreightCalculator.Calculate(c, r, Query(vehicle: Truck32, km: 120m), null).Value.Notes.ShouldContain(n => n.Contains("minimum 200 km"));
    }

    [Fact]
    public void A_per_km_rate_without_a_distance_is_an_error_not_a_guess()
    {
        var (c, r) = FtlRate(new PerKmPricing(38m, 0m, 0m));

        FreightCalculator.Calculate(c, r, Query(vehicle: Truck32), null).Error.Code.ShouldBe("quote.distance_required");
    }

    // ---------------- PTL ----------------

    [Theory]
    [InlineData(50, 500)]       // slab 0–100 at ₹10
    [InlineData(100, 1000)]     // 100 is the top of the first slab (upper limit inclusive)
    [InlineData(101, 808)]      // 101 falls in 100–500 at ₹8
    [InlineData(500, 4000)]
    [InlineData(501, 3006)]     // above 500 at ₹6
    public void Whole_mode_applies_the_slab_rate_to_the_entire_weight(double kg, double expected)
    {
        var (c, r) = PtlRate(Slabs(SlabMode.Whole));

        Total(c, r, Query(weight: (decimal)kg)).ShouldBe((decimal)expected);
    }

    [Fact]
    public void Incremental_mode_charges_each_slice_at_its_own_rate()
    {
        var (c, r) = PtlRate(Slabs(SlabMode.Incremental));

        Total(c, r, Query(weight: 50m)).ShouldBe(500m);
        Total(c, r, Query(weight: 150m)).ShouldBe(1_400m, "100 × 10 + 50 × 8");
        Total(c, r, Query(weight: 700m)).ShouldBe(5_400m, "100×10 + 400×8 + 200×6 = 1000 + 3200 + 1200");
    }

    [Fact]
    public void Volumetric_weight_is_charged_when_it_exceeds_actual_weight()
    {
        var (c, r) = PtlRate(Slabs(), terms: Terms(volumetric: 300m));

        var quote = FreightCalculator.Calculate(c, r, Query(weight: 100m, volume: 2m), null).Value; // 2 CBM × 300 = 600 kg

        quote.ChargeableWeightKg.ShouldBe(600m);
        quote.Total.ShouldBe(3_600m, "600 kg × ₹6");
        quote.Notes.ShouldContain(n => n.StartsWith("Volumetric weight 600"));
    }

    [Fact]
    public void Actual_weight_wins_when_it_is_higher_than_volumetric()
    {
        var (c, r) = PtlRate(Slabs());

        FreightCalculator.Calculate(c, r, Query(weight: 400m, volume: 0.5m), null).Value.ChargeableWeightKg.ShouldBe(400m);
    }

    [Fact]
    public void Minimum_chargeable_weight_and_minimum_charge_floor_small_consignments()
    {
        var (c, r) = PtlRate(Slabs(minCharge: 750m, minKg: 40m));

        var tiny = FreightCalculator.Calculate(c, r, Query(weight: 5m), null).Value;
        tiny.ChargeableWeightKg.ShouldBe(40m);
        tiny.Total.ShouldBe(750m, "40 kg × 10 = 400, raised to the 750 minimum");
        tiny.Lines[0].Description.ShouldContain("minimum charge");
    }

    [Fact]
    public void The_contract_wide_minimum_per_consignment_also_applies()
    {
        var (c, r) = PtlRate(Slabs(minCharge: 100m), terms: Terms(minConsignment: 600m));

        Total(c, r, Query(weight: 20m)).ShouldBe(600m);
    }

    [Fact]
    public void A_part_load_without_a_weight_is_an_error()
    {
        var (c, r) = PtlRate(Slabs());

        FreightCalculator.Calculate(c, r, Query(), null).Error.Code.ShouldBe("quote.weight_required");
    }

    // ---------------- fuel and accessorials ----------------

    [Fact]
    public void The_fuel_adjustment_is_its_own_line_calculated_on_base_freight_only()
    {
        var (c, r) = FtlRate(new FlatTripPricing(40_000m), Fuel(), Terms(loading: 500m));

        var quote = FreightCalculator.Calculate(c, r, Query(vehicle: Truck32), dieselPricePerLitre: 95m).Value;

        quote.Lines.Select(l => l.Code).ShouldBe(["FREIGHT", "FUEL", "LOADING"]);
        quote.Lines[1].Amount.ShouldBe(1_000m, "+2.5% of 40,000 — not of the loading charge");
        quote.Total.ShouldBe(41_500m);
    }

    [Fact]
    public void De_escalation_produces_a_negative_fuel_line()
    {
        var (c, r) = FtlRate(new FlatTripPricing(40_000m), Fuel());

        var quote = FreightCalculator.Calculate(c, r, Query(vehicle: Truck32), 85m).Value;

        quote.Lines[1].Amount.ShouldBe(-1_000m);
        quote.Total.ShouldBe(39_000m);
    }

    [Fact]
    public void Without_a_diesel_price_the_quote_still_works_and_says_why_there_is_no_fuel_line()
    {
        var (c, r) = FtlRate(new FlatTripPricing(40_000m), Fuel());

        var quote = FreightCalculator.Calculate(c, r, Query(vehicle: Truck32), null).Value;

        quote.Lines.ShouldHaveSingleItem();
        quote.Notes.ShouldContain(n => n.Contains("No diesel price on file for DELHI"));
    }

    [Fact]
    public void A_price_inside_the_tolerance_adds_no_line_but_is_explained()
    {
        var (c, r) = FtlRate(new FlatTripPricing(40_000m), Fuel(deadBand: 5m));

        var quote = FreightCalculator.Calculate(c, r, Query(vehicle: Truck32), 92m).Value;

        quote.Lines.ShouldHaveSingleItem();
        quote.Notes.ShouldContain(n => n.Contains("within the tolerance"));
    }

    [Fact]
    public void Multi_drop_loading_and_unloading_are_itemised()
    {
        var (c, r) = FtlRate(new FlatTripPricing(10_000m), terms: Terms(loading: 300m, unloading: 250m, multiDrop: 400m));

        var quote = FreightCalculator.Calculate(c, r, Query(vehicle: Truck32, drops: 4), null).Value;

        quote.Lines.Select(l => (l.Code, l.Amount)).ShouldBe([("FREIGHT", 10_000m), ("LOADING", 300m), ("UNLOADING", 250m), ("MULTI_DROP", 1_200m)]);
        quote.Total.ShouldBe(11_750m);
    }

    [Fact]
    public void Rounding_is_to_paise_per_line_so_the_total_is_exactly_the_sum_shown()
    {
        var (c, r) = PtlRate(new WeightSlabPricing(SlabMode.Whole, [new WeightSlab(0, null, 3.333m)], 0, 0), Fuel());

        var quote = FreightCalculator.Calculate(c, r, Query(weight: 7m), 95m).Value; // 23.331 → 23.33; fuel 2.5% of 23.33 = 0.58325 → 0.58

        quote.Lines.Select(l => l.Amount).ShouldBe([23.33m, 0.58m]);
        quote.Total.ShouldBe(23.91m);
        FreightCalculator.Round(2.345m).ShouldBe(2.35m, "half rounds away from zero");
        FreightCalculator.Round(-2.345m).ShouldBe(-2.35m);
    }

    // ---------------- dedicated ----------------

    [Fact]
    public void A_dedicated_month_is_rental_plus_overage_beyond_the_allowances()
    {
        var pricing = new DedicatedPricing(MonthlyRental: 95_000m, IncludedKmPerMonth: 3_000m, ExtraKmRate: 14m, IncludedHoursPerMonth: 260m, ExtraHourRate: 250m);
        var contract = Active(ContractType.Dedicated, [Spec(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), pricing, Truck14)]);

        var within = FreightCalculator.CalculateDedicatedMonth(contract, contract.RateCards.Single(), 2_900m, 250m, null).Value;
        within.Total.ShouldBe(95_000m);

        var over = FreightCalculator.CalculateDedicatedMonth(contract, contract.RateCards.Single(), 3_500m, 280m, null).Value;
        over.Lines.Select(l => (l.Code, l.Amount)).ShouldBe([("RENTAL", 95_000m), ("EXTRA_KM", 7_000m), ("EXTRA_HOURS", 5_000m)]);
        over.Total.ShouldBe(107_000m);
    }

    [Fact]
    public void A_dedicated_rate_cannot_be_quoted_per_shipment()
    {
        var contract = Active(ContractType.Dedicated, [Spec(Place.OfState("Goa"), Place.OfState("Goa"), new DedicatedPricing(80_000m, 2_000m, 12m, 0m, 0m), Truck14)]);

        FreightCalculator.Calculate(contract, contract.RateCards.Single(), Query(), null).Error.Code.ShouldBe("quote.dedicated_monthly");
    }
}
