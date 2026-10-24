using Tms.Modules.TransportOps.Domain;

namespace Tms.UnitTests.TransportOps;

public class GeofenceTests
{
    // Singer's Pune DC gate.
    private const double Lat = 18.7606, Lon = 73.8636;

    [Fact]
    public void A_fix_inside_the_radius_with_good_accuracy_is_accepted()
    {
        var d = GeofenceVerifier.Verify(Lat, Lon, 150, Lat + 0.0005, Lon, 12, 100); // about 55 m

        d.Accepted.ShouldBeTrue();
        d.DistanceM.ShouldNotBeNull().ShouldBeInRange(40, 70);
    }

    [Fact]
    public void A_fix_outside_the_radius_is_refused_and_says_how_far_away_it_is()
    {
        var d = GeofenceVerifier.Verify(Lat, Lon, 150, Lat + 0.01, Lon, 10, 100);

        d.Check.ShouldBe(ArrivalCheck.Outside);
        d.Accepted.ShouldBeFalse();
        d.Message.ShouldContain("within 150 m");
    }

    [Theory]
    [InlineData(null, 73.8, 10.0)]
    [InlineData(18.7, null, 10.0)]
    public void A_missing_position_is_never_treated_as_arrival(double? lat, double? lon, double? accuracy)
    {
        GeofenceVerifier.Verify(Lat, Lon, 150, lat, lon, accuracy, 100).Check.ShouldBe(ArrivalCheck.NoLocation);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(250.0)]
    [InlineData(-5.0)]
    public void A_position_that_is_too_vague_is_refused_even_when_it_is_at_the_gate(double? accuracy)
    {
        GeofenceVerifier.Verify(Lat, Lon, 150, Lat, Lon, accuracy, 100).Check.ShouldBe(ArrivalCheck.AccuracyInsufficient);
    }

    [Fact]
    public void A_destination_without_coordinates_cannot_verify_arrival()
    {
        GeofenceVerifier.Verify(null, null, 150, Lat, Lon, 5, 100).Check.ShouldBe(ArrivalCheck.DestinationHasNoCoordinates);
    }

    [Fact]
    public void The_radius_belongs_to_the_location_so_the_same_distance_can_pass_at_one_gate_and_fail_at_another()
    {
        var lat = Lat + 0.002; // about 222 m
        GeofenceVerifier.Verify(Lat, Lon, 150, lat, Lon, 10, 100).Accepted.ShouldBeFalse();
        GeofenceVerifier.Verify(Lat, Lon, 300, lat, Lon, 10, 100).Accepted.ShouldBeTrue();
    }
}

public class DetentionTests
{
    private static readonly DateTimeOffset Ist0 = new(2026, 10, 5, 9, 0, 0, TimeSpan.FromMinutes(330));

    private static DetentionPolicy Hours(decimal free, decimal rate, PartUnitRounding rounding = PartUnitRounding.RoundUp, decimal? cap = null) =>
        new(true, FreeTimeKind.Duration, free, DetentionUnit.Hour, DetentionUnit.Hour, rate, rounding, cap, null);

    [Fact]
    public void Nothing_is_charged_when_unloading_finishes_within_the_free_time()
    {
        var r = DetentionCalculator.Calculate(Hours(4, 200), Ist0, Ist0.AddHours(3.5));

        r.Status.ShouldBe(DetentionStatus.Calculated);
        r.Amount.ShouldBe(0);
    }

    [Fact]
    public void Only_the_time_beyond_the_free_allowance_is_charged_and_a_started_hour_counts_in_full()
    {
        var r = DetentionCalculator.Calculate(Hours(4, 200), Ist0, Ist0.AddHours(6).AddMinutes(10)); // 2h10 over

        r.ChargeableHours.ShouldBe(2.1667, 0.001);
        r.Units.ShouldBe(3);
        r.Amount.ShouldBe(600m);
    }

    [Theory]
    [InlineData(PartUnitRounding.RoundUp, 3)]
    [InlineData(PartUnitRounding.RoundDown, 2)]
    [InlineData(PartUnitRounding.Nearest, 2)]
    [InlineData(PartUnitRounding.Exact, 2.17)]
    public void The_customers_rounding_rule_decides_how_a_part_hour_is_charged(PartUnitRounding rounding, double expectedUnits)
    {
        var r = DetentionCalculator.Calculate(Hours(4, 100, rounding), Ist0, Ist0.AddHours(6).AddMinutes(10));

        r.Units.ShouldBe((decimal)expectedUnits);
    }

    [Fact]
    public void The_agreed_maximum_limits_the_charge()
    {
        var r = DetentionCalculator.Calculate(Hours(4, 200, cap: 1000), Ist0, Ist0.AddHours(24));

        r.Amount.ShouldBe(1000m);
        r.CapApplied.ShouldBeTrue();
    }

    [Fact]
    public void An_end_of_day_rule_gives_free_time_until_midnight_india_time_and_charges_after_it()
    {
        var policy = new DetentionPolicy(true, FreeTimeKind.EndOfDay, 0, DetentionUnit.Hour, DetentionUnit.Day, 1500, PartUnitRounding.RoundUp, null, null);
        var arrival = new DateTimeOffset(2026, 10, 5, 20, 0, 0, TimeSpan.FromMinutes(330)); // 8 pm

        DetentionCalculator.Calculate(policy, arrival, new DateTimeOffset(2026, 10, 5, 23, 30, 0, TimeSpan.FromMinutes(330))).Amount.ShouldBe(0);
        var next = DetentionCalculator.Calculate(policy, arrival, new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromMinutes(330))); // 10 hours after midnight

        next.Units.ShouldBe(1); // a started day
        next.Amount.ShouldBe(1500m);
    }

    [Fact]
    public void Free_days_can_be_counted_in_days_while_the_charge_is_per_day()
    {
        var policy = new DetentionPolicy(true, FreeTimeKind.Duration, 2, DetentionUnit.Day, DetentionUnit.Day, 800, PartUnitRounding.RoundUp, null, null);

        var r = DetentionCalculator.Calculate(policy, Ist0, Ist0.AddDays(4).AddHours(1)); // 2 days and 1 hour over

        r.Units.ShouldBe(3);
        r.Amount.ShouldBe(2400m);
    }

    [Fact]
    public void A_customer_without_detention_is_never_charged_and_is_not_held()
    {
        var r = DetentionCalculator.Calculate(DetentionPolicy.None, null, null);

        r.Status.ShouldBe(DetentionStatus.NotApplicable);
        r.Amount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void A_missing_time_puts_detention_on_hold_instead_of_assuming_zero(bool hasArrival, bool hasUnloading)
    {
        var r = DetentionCalculator.Calculate(Hours(4, 200), hasArrival ? Ist0 : null, hasUnloading ? Ist0.AddHours(8) : null);

        r.Status.ShouldBe(DetentionStatus.OnHold);
        r.HoldReason.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public void Unloading_before_arrival_is_an_inconsistency_to_fix_not_a_zero()
    {
        DetentionCalculator.Calculate(Hours(4, 200), Ist0, Ist0.AddHours(-1)).Status.ShouldBe(DetentionStatus.OnHold);
    }

    [Fact]
    public void The_same_inputs_always_give_the_same_answer()
    {
        var a = DetentionCalculator.Calculate(Hours(4, 200), Ist0, Ist0.AddHours(9));
        var b = DetentionCalculator.Calculate(Hours(4, 200), Ist0, Ist0.AddHours(9));

        (a.Amount, a.Units, a.FreeUntil, a.ChargeableHours).ShouldBe((b.Amount, b.Units, b.FreeUntil, b.ChargeableHours));
        a.Trace.ShouldBe(b.Trace);
    }
}

public class FreightTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Mumbai = Guid.NewGuid(), Pune = Guid.NewGuid();

    private static CustomerAgreement Agreement(WeightBasis basis = WeightBasis.Actual, decimal? factor = null, decimal? minimum = null, params AgreementRate[] rates)
    {
        var a = CustomerAgreement.Create(Tenant, "AGR-1", Guid.NewGuid(), "Test", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31));
        a.Edit("Test", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), basis, factor, minimum, DetentionPolicy.None, TaxSettings.None, BillingRules.Standard, []);
        a.SetRates(rates);
        a.Activate(null, DateTimeOffset.UtcNow);
        return a;
    }

    private static AgreementRate Rate(RateMethod method, decimal rate, Guid? from = null, Guid? to = null, string? fromState = null, string? toState = null, string? material = null, decimal? min = null, params WeightSlab[] slabs) =>
        AgreementRate.Create(Tenant, Guid.NewGuid(), from, null, fromState, to, null, toState, material, method, rate, slabs, min);

    private static LrFacts Lr(decimal kg = 1000, decimal? cbm = null, string? material = "Soap", DateOnly? date = null) =>
        new(date ?? new DateOnly(2026, 10, 5), Mumbai, "Mumbai", "Maharashtra", Pune, "Pune", "Maharashtra", material, kg, cbm);

    [Fact]
    public void Per_kg_freight_is_weight_times_rate_and_the_breakdown_explains_it()
    {
        var a = Agreement(rates: Rate(RateMethod.PerKg, 3.5m, Mumbai, Pune));

        var r = CustomerFreightCalculator.Calculate(a, Lr(1200)).Value;

        r.BasicFreight.ShouldBe(4200m);
        r.Total.ShouldBe(4200m);
        r.Lines.Single().Basis.ShouldContain("1200");
        r.Trace.ShouldContain(t => t.Contains("AGR-1"));
    }

    [Fact]
    public void Per_tonne_freight_divides_the_weight_by_a_thousand()
    {
        var a = Agreement(rates: Rate(RateMethod.PerTonne, 2800m, Mumbai, Pune));

        CustomerFreightCalculator.Calculate(a, Lr(8500)).Value.BasicFreight.ShouldBe(23800m);
    }

    [Fact]
    public void A_weight_band_prices_the_whole_weight_at_the_rate_of_its_band()
    {
        var a = Agreement(rates: Rate(RateMethod.WeightSlabs, 0, Mumbai, Pune, slabs: [new WeightSlab(0, 500, 6m), new WeightSlab(500, 2000, 4m), new WeightSlab(2000, null, 3m)]));

        CustomerFreightCalculator.Calculate(a, Lr(500)).Value.BasicFreight.ShouldBe(3000m); // 500 belongs to the first band (up to and including)
        CustomerFreightCalculator.Calculate(a, Lr(501)).Value.BasicFreight.ShouldBe(2004m);
        CustomerFreightCalculator.Calculate(a, Lr(5000)).Value.BasicFreight.ShouldBe(15000m);
    }

    [Fact]
    public void The_minimum_freight_of_the_rate_and_of_the_agreement_are_applied_in_turn_and_shown_as_lines()
    {
        var a = Agreement(minimum: 2500m, rates: Rate(RateMethod.PerKg, 2m, Mumbai, Pune, min: 1500m));

        var r = CustomerFreightCalculator.Calculate(a, Lr(100)).Value; // 200 -> 1500 (rate minimum) -> 2500 (agreement minimum)

        r.BasicFreight.ShouldBe(2500m);
        r.Lines.Select(l => l.Code).ShouldBe(["BASE_FREIGHT", "RATE_MINIMUM", "MIN_FREIGHT"]);
    }

    [Fact]
    public void Other_agreed_charges_are_separate_lines_and_stay_out_of_basic_freight()
    {
        var a = Agreement(rates: Rate(RateMethod.FlatPerConsignment, 10000m, Mumbai, Pune));
        a.Edit("Test", a.EffectiveFrom, a.EffectiveTo, a.WeightBasis, null, null, DetentionPolicy.None, TaxSettings.None, BillingRules.Standard, [new ExtraCharge("UNLOAD", "Unloading", 500m)]);

        var r = CustomerFreightCalculator.Calculate(a, Lr()).Value;

        (r.BasicFreight, r.OtherCharges, r.Total).ShouldBe((10000m, 500m, 10500m));
    }

    [Fact]
    public void The_billable_weight_follows_the_agreed_basis()
    {
        var rates = Rate(RateMethod.PerKg, 1m, Mumbai, Pune);

        CustomerFreightCalculator.Calculate(Agreement(WeightBasis.Actual, 250, null, rates), Lr(1000, 8)).Value.BillableWeightKg.ShouldBe(1000m);
        CustomerFreightCalculator.Calculate(Agreement(WeightBasis.Volumetric, 250, null, rates), Lr(1000, 8)).Value.BillableWeightKg.ShouldBe(2000m);
        var higher = CustomerFreightCalculator.Calculate(Agreement(WeightBasis.HigherOfActualAndVolumetric, 250, null, rates), Lr(3000, 8)).Value;
        higher.BillableWeightKg.ShouldBe(3000m);
        higher.WeightBasisUsed.ShouldBe("Actual");
    }

    [Fact]
    public void A_volumetric_basis_without_a_volume_is_an_error_not_a_guess()
    {
        var r = CustomerFreightCalculator.Calculate(Agreement(WeightBasis.Volumetric, 250, null, Rate(RateMethod.PerKg, 1m, Mumbai, Pune)), Lr(1000, null));

        r.IsFailure.ShouldBeTrue();
        r.Error.Code.ShouldBe("OPS_INPUT_MISSING");
    }

    [Fact]
    public void The_most_specific_rate_wins_and_a_material_rate_beats_a_general_one()
    {
        var a = Agreement(rates: [
            Rate(RateMethod.PerKg, 5m, fromState: "Maharashtra", toState: "Maharashtra"),
            Rate(RateMethod.PerKg, 4m, Mumbai, Pune),
            Rate(RateMethod.PerKg, 3m, Mumbai, Pune, material: "soap")]);

        CustomerFreightCalculator.Calculate(a, Lr(100, material: "Soap")).Value.BasicFreight.ShouldBe(300m);
        CustomerFreightCalculator.Calculate(a, Lr(100, material: "Shampoo")).Value.BasicFreight.ShouldBe(400m);
    }

    [Fact]
    public void Two_equally_specific_rates_are_a_conflict_never_a_silent_pick()
    {
        var a = Agreement(rates: [Rate(RateMethod.PerKg, 5m, Mumbai, Pune), Rate(RateMethod.PerKg, 6m, Mumbai, Pune)]);

        var r = CustomerFreightCalculator.Calculate(a, Lr());

        r.IsFailure.ShouldBeTrue();
        r.Error.Code.ShouldBe("OPS_RATE_CONFLICT");
    }

    [Fact]
    public void A_lane_with_no_rate_is_reported()
    {
        var r = CustomerFreightCalculator.Calculate(Agreement(rates: Rate(RateMethod.PerKg, 5m, fromState: "Gujarat")), Lr());

        r.Error.Code.ShouldBe("OPS_RATE_NOT_FOUND");
    }

    [Fact]
    public void An_agreement_is_only_used_inside_its_dates()
    {
        var a = Agreement(rates: Rate(RateMethod.PerKg, 5m, Mumbai, Pune));

        CustomerFreightCalculator.Calculate(a, Lr(date: new DateOnly(2027, 4, 1))).Error.Code.ShouldBe("OPS_NO_AGREEMENT");
        CustomerFreightCalculator.Calculate(a, Lr(date: new DateOnly(2026, 3, 31))).Error.Code.ShouldBe("OPS_NO_AGREEMENT");
    }

    [Fact]
    public void The_calculation_is_reproducible()
    {
        var a = Agreement(WeightBasis.HigherOfActualAndVolumetric, 200, 1000m, Rate(RateMethod.PerKg, 3.3333m, Mumbai, Pune));

        var first = CustomerFreightCalculator.Calculate(a, Lr(777.7m, 4.2m)).Value;
        var second = CustomerFreightCalculator.Calculate(a, Lr(777.7m, 4.2m)).Value;

        second.Total.ShouldBe(first.Total);
        second.Lines.ShouldBe(first.Lines);
    }
}

public class TaxAndNumberingTests
{
    [Fact]
    public void Within_one_state_the_tax_is_split_between_central_and_state()
    {
        var t = TaxCalculator.Compute(10000m, new TaxSettings(TaxMode.Forward, 12, "996511", null), "27", "27").Value;

        (t.Cgst, t.Sgst, t.Igst, t.Total).ShouldBe((600m, 600m, 0m, 1200m));
    }

    [Fact]
    public void Across_states_it_is_integrated_tax()
    {
        var t = TaxCalculator.Compute(10000m, new TaxSettings(TaxMode.Forward, 5, null, null), "27", "29").Value;

        (t.Cgst, t.Sgst, t.Igst).ShouldBe((0m, 0m, 500m));
    }

    [Fact]
    public void Reverse_charge_and_no_tax_add_nothing_to_the_invoice()
    {
        TaxCalculator.Compute(10000m, new TaxSettings(TaxMode.ReverseCharge, 5, null, null), "27", "29").Value.Total.ShouldBe(0);
        TaxCalculator.Compute(10000m, TaxSettings.None, null, null).Value.Total.ShouldBe(0);
    }

    [Fact]
    public void Tax_is_never_guessed_when_the_states_are_unknown_or_the_rate_is_missing()
    {
        TaxCalculator.Compute(100m, new TaxSettings(TaxMode.Forward, 12, null, null), null, "29").Error.Code.ShouldBe("OPS_TAX_PLACE_OF_SUPPLY");
        TaxCalculator.Compute(100m, new TaxSettings(TaxMode.Forward, 0, null, null), "27", "29").Error.Code.ShouldBe("OPS_TAX_RATE");
    }

    [Theory]
    [InlineData(2026, 4, 1, "26-27")]
    [InlineData(2027, 3, 31, "26-27")]
    [InlineData(2026, 10, 23, "26-27")]
    [InlineData(2027, 4, 1, "27-28")]
    [InlineData(2026, 1, 15, "25-26")]
    public void The_financial_year_runs_from_april_to_march(int y, int m, int d, string expected)
    {
        FinancialYear.Label(new DateOnly(y, m, d)).ShouldBe(expected);
    }
}
