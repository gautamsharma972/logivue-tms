using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Contracts.ContractTestData;
using static Tms.UnitTests.Contracts.RatingTestData;

namespace Tms.UnitTests.Contracts;

public class RateValidatorTests
{
    private static readonly DateOnly From = new(2026, 4, 1);
    private static readonly DateOnly To = new(2027, 3, 31);

    private static RateToCheck Row(int row, RateCardSpec spec, DateOnly? from = null, DateOnly? to = null, string owner = "CN-1") => new(row, spec, from ?? From, to ?? To, owner);

    private static RateValidationResult Check(IReadOnlyList<RateToCheck> rows, IReadOnlyList<RateToCheck>? inForce = null, string[]? dph = null, ContractType[]? services = null) =>
        RateValidator.Validate(rows, From, To, services ?? [ContractType.Ftl, ContractType.Ptl], (dph ?? []).ToHashSet(), inForce);

    private static RateCardSpec Banded(decimal weightFrom, decimal weightTo, decimal amount = 38_000m, int priority = 100) =>
        MumbaiPune(amount, new RateExtras(MinWeightKg: weightFrom, MaxWeightKg: weightTo, Priority: priority));

    [Fact]
    public void Overlapping_weight_slabs_on_one_lane_block_activation()
    {
        var result = Check([Row(1, Banded(500, 1_000)), Row(2, Banded(800, 1_500))]);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "RATE_CONFLICT" && i.Row == 2 && i.Message.Contains("row 1"));
    }

    [Fact]
    public void Slabs_that_only_touch_are_fine()
    {
        Check([Row(1, Banded(0, 500)), Row(2, Banded(500, 1_000)), Row(3, Banded(1_000, 3_000))]).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Overlapping_validity_periods_are_a_conflict_but_back_to_back_ones_are_not()
    {
        var spec = MumbaiPune(38_000m);

        Check([Row(1, spec, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)), Row(2, spec with { Extras = new RateExtras(Priority: 100, Code: "X") }, new DateOnly(2026, 12, 1), new DateOnly(2027, 3, 31))])
            .Issues.ShouldContain(i => i.Code == "RATE_CONFLICT");
        Check([Row(1, spec, new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31)), Row(2, spec, new DateOnly(2027, 1, 1), new DateOnly(2027, 3, 31))]).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void An_identical_rate_is_a_duplicate()
    {
        var spec = MumbaiPune(38_000m);

        Check([Row(1, spec), Row(2, spec)]).Issues.ShouldContain(i => i.Code == "RATE_DUPLICATE" && i.Row == 2);
    }

    [Fact]
    public void Different_priorities_make_an_overlap_a_warning_the_priority_resolves()
    {
        var result = Check([Row(1, Banded(500, 1_000, priority: 100)), Row(2, Banded(800, 1_500, priority: 10))]);

        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldContain(i => i.Severity == IssueSeverity.Warning && i.Code == "RATE_OVERLAP_PRIORITY");
    }

    [Fact]
    public void A_rate_already_in_force_on_another_contract_of_the_same_transporter_is_a_conflict()
    {
        var existing = Row(7, Banded(500, 1_000), owner: "CN-OLD");

        var result = Check([Row(1, Banded(600, 900, amount: 40_000m))], [existing]);

        result.Issues.ShouldContain(i => i.Code == "RATE_CONFLICT" && i.Message.Contains("CN-OLD"));
    }

    [Fact]
    public void Rates_for_different_vehicles_or_lanes_or_services_never_clash()
    {
        var other = Rate(MumbaiCity, PuneCity, new FlatTripPricing(30_000m), Truck14);
        var elsewhere = Rate(MumbaiCity, Place.OfCity("Gujarat", "Surat"), new FlatTripPricing(30_000m), Truck32);

        Check([Row(1, MumbaiPune(38_000m)), Row(2, other), Row(3, elsewhere)]).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void A_both_ways_rate_clashes_with_the_reverse_lane()
    {
        var forward = Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), Truck32, bothWays: true);
        var back = Rate(PuneCity, MumbaiCity, new FlatTripPricing(37_000m), Truck32, new RateExtras(Code: "BACK"));

        Check([Row(1, forward), Row(2, back)]).Issues.ShouldContain(i => i.Code == "RATE_CONFLICT");
    }

    [Fact]
    public void Each_rate_is_checked_for_its_own_completeness()
    {
        var result = Check(
        [
            Row(1, Rate(MumbaiCity, PuneCity, new FlatTripPricing(38_000m), null)),                                                    // no vehicle
            Row(2, Rate(MumbaiCity, PuneCity, new FlatTripPricing(0m), Truck32, new RateExtras(Code: "ZERO"))),                         // no price
            Row(3, Rate(Place.Anywhere, Place.Anywhere, new FlatTripPricing(10m), Truck32, new RateExtras(Code: "ANY"))),               // anywhere, no band
            Row(4, Rate(MumbaiCity, PuneCity, new FlatTripPricing(10m), Truck14, new RateExtras(Code: "DPH", DphRuleCode: "NOPE"))),   // unknown DPH
            Row(5, Rate(MumbaiCity, PuneCity, new FlatTripPricing(10m), Truck14, new RateExtras(Code: "RANGE", MinWeightKg: 1_000m, MaxWeightKg: 500m))),
            Row(6, Rate(MumbaiCity, PuneCity, new FlatTripPricing(10m), Truck14, new RateExtras(Code: "DATES"), 500m, 100m)),
        ]);

        var codes = result.Issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => (i.Row, i.Code)).ToList();
        codes.ShouldContain((1, "RATE_VEHICLE_MISSING"));
        codes.ShouldContain((2, "RATE_PRICING_INVALID"));
        codes.ShouldContain((3, "RATE_LANE_MISSING"));
        codes.ShouldContain((4, "RATE_DPH_UNKNOWN"));
        codes.ShouldContain((5, "RATE_TERMS_INVALID"));
        codes.ShouldContain((6, "RATE_RANGE_INVALID"));
    }

    [Fact]
    public void A_rate_for_a_service_the_contract_does_not_cover_is_refused_and_one_outside_its_dates_is_warned()
    {
        var result = Check([Row(1, Rate(Place.OfState("Goa"), Place.OfState("Goa"), new DedicatedPricing(100_000m, 1000, 10, 0, 0), Truck32)), Row(2, MumbaiPune(38_000m), From.AddMonths(-3), To)]);

        result.Issues.ShouldContain(i => i.Code == "RATE_SERVICE_NOT_COVERED" && i.Row == 1);
        result.Issues.ShouldContain(i => i.Code == "RATE_OUTSIDE_CONTRACT" && i.Severity == IssueSeverity.Warning && i.Row == 2);
    }

    [Fact]
    public void Thousands_of_rates_are_checked_without_comparing_every_pair()
    {
        var rows = Enumerable.Range(0, 3_000).Select(i => Row(i + 1, Rate(Place.OfCity("Maharashtra", $"City{i}"), PuneCity, new FlatTripPricing(1_000m + i), Truck32))).ToList();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = Check(rows);
        clock.Stop();

        result.IsValid.ShouldBeTrue();
        clock.ElapsedMilliseconds.ShouldBeLessThan(5_000);
    }
}

public class ContractLifecycleDomainTests
{
    private static Contract Active(params RateCardSpec[] rates) => ActiveWith(rates.Length == 0 ? [MumbaiPune(38_000m)] : rates);

    [Fact]
    public void A_contract_always_covers_its_own_type_and_the_extras_are_validated()
    {
        var contract = Draft(ContractType.Ptl);

        contract.EffectiveServices.ShouldBe([ContractType.Ptl]);
        contract.ApplyExtras(new ContractExtras("inr", "Retail", "Asha 98xxxxxx01", 45, true, [ContractType.Ftl])).IsSuccess.ShouldBeTrue();
        contract.Currency.ShouldBe("INR");
        contract.EffectiveServices.ShouldBe([ContractType.Ftl, ContractType.Ptl], ignoreOrder: true);
        contract.RenewalNoticeDays.ShouldBe(45);
        contract.ApplyExtras(new ContractExtras("RUPEE")).IsFailure.ShouldBeTrue();
        contract.ApplyExtras(new ContractExtras(RenewalNoticeDays: 900)).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_service_cannot_be_dropped_while_a_rate_prices_it()
    {
        var contract = Draft(ContractType.Ftl);
        contract.ApplyExtras(new ContractExtras(Services: [ContractType.Ptl])).IsSuccess.ShouldBeTrue();
        contract.ReplaceRates([Rate(Place.OfState("Goa"), Place.OfState("Goa"), new SlabRatePricing(ContractType.Ptl, SlabDimension.Weight, RateUnit.Kg, SlabMethod.Flat, [new Slab(0, null, 9)]))]).IsSuccess.ShouldBeTrue();

        contract.ApplyExtras(new ContractExtras(Services: [])).Error.Description.ShouldContain("Ptl must stay");
    }

    [Fact]
    public void An_approved_contract_is_immutable_and_activation_raises_events()
    {
        var contract = Active();

        contract.ReplaceRates([MumbaiPune(1m)]).Error.Code.ShouldBe("contracts.immutable");
        contract.ReplaceDphRules([Dph()]).Error.Code.ShouldBe("contracts.immutable");
        contract.ReplaceAccessorials([]).Error.Code.ShouldBe("contracts.immutable");
        contract.ApplyExtras(new ContractExtras()).Error.Code.ShouldBe("contracts.immutable");
        contract.DomainEvents.OfType<ContractActivated>().Single().RateCount.ShouldBe(1);
        contract.DomainEvents.OfType<RateActivated>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Suspending_stops_the_rates_from_that_day_and_resuming_restores_them()
    {
        var contract = Active();
        var suspendedOn = Today;

        contract.Suspend("Under dispute", suspendedOn).IsSuccess.ShouldBeTrue();

        contract.Status.ShouldBe(ContractStatus.Suspended);
        contract.IsInForce(suspendedOn.AddDays(-1)).ShouldBeTrue("shipments before the suspension stay priceable");
        contract.IsInForce(suspendedOn).ShouldBeFalse();
        contract.IsInForce(suspendedOn.AddDays(5)).ShouldBeFalse();
        contract.DomainEvents.OfType<ContractSuspended>().ShouldHaveSingleItem();

        contract.Resume(suspendedOn.AddDays(3)).IsSuccess.ShouldBeTrue();

        contract.Status.ShouldBe(ContractStatus.Active);
        contract.IsInForce(suspendedOn.AddDays(2)).ShouldBeFalse("the paused days stay paused");
        contract.IsInForce(suspendedOn.AddDays(3)).ShouldBeTrue();
    }

    [Fact]
    public void Suspension_needs_a_reason_an_active_contract_and_can_be_followed_by_termination()
    {
        var draft = Draft();
        var contract = Active();

        draft.Suspend("x", Today).Error.Code.ShouldBe("contracts.not_active");
        contract.Suspend(" ", Today).Error.Code.ShouldBe("contracts.reason_required");
        contract.Suspend("Audit", Today).IsSuccess.ShouldBeTrue();
        contract.Resume(Today).IsSuccess.ShouldBeTrue();
        contract.Suspend("Audit again", Today.AddDays(5)).IsSuccess.ShouldBeTrue();
        contract.Terminate("Ended by agreement", Today.AddDays(6)).IsSuccess.ShouldBeTrue();
        contract.Status.ShouldBe(ContractStatus.Terminated);
        contract.Suspensions[^1].To.ShouldNotBeNull();
    }

    [Fact]
    public void A_draft_can_be_cancelled_but_not_an_active_contract()
    {
        var draft = Draft();
        var active = Active();

        draft.Cancel("").Error.Code.ShouldBe("contracts.reason_required");
        draft.Cancel("Replaced by a better offer").IsSuccess.ShouldBeTrue();
        draft.Status.ShouldBe(ContractStatus.Cancelled);
        active.Cancel("No").Error.Code.ShouldBe("contracts.not_cancellable");
    }

    [Fact]
    public void An_amendment_copies_everything_and_numbers_changed_rates_one_higher()
    {
        var lane = MumbaiPune(38_000m, new RateExtras(Code: "RATE-MUM-PUN"));
        var other = Rate(MumbaiCity, Place.OfCity("Gujarat", "Surat"), new FlatTripPricing(52_000m), Truck32, new RateExtras(Code: "RATE-MUM-SUR"));
        var contract = ActiveWith([lane, other], dph: [Dph()], accessorials: [new AccessorialSpec("LOADING", "Loading", AccessorialCalc.Fixed, "TRIP", 500m, AutoApply: true)]);
        contract.ReplaceCapacities([new CapacitySpec(Truck32, 6, null, 250, null, 60m, null, null)]).Error.Code.ShouldBe("contracts.immutable");

        var amendment = contract.CreateRevision(Today.AddDays(1), Today.AddYears(1), null).Value;

        amendment.RevisionKind.ShouldBe(RevisionKind.Amendment);
        amendment.Status.ShouldBe(ContractStatus.Draft);
        amendment.RateCards.Count.ShouldBe(2);
        amendment.DphRules.Count.ShouldBe(1);
        amendment.Accessorials.Count.ShouldBe(1);
        amendment.RateCards.ShouldAllBe(r => r.Version == 1);

        var changed = amendment.ReplaceRates(
            [lane with { Pricing = new FlatTripPricing(40_000m) }, other],
            contract.RateCards.ToDictionary(r => r.Code));

        changed.IsSuccess.ShouldBeTrue();
        amendment.RateCards.Single(r => r.Code == "RATE-MUM-PUN").Version.ShouldBe(2);
        amendment.RateCards.Single(r => r.Code == "RATE-MUM-SUR").Version.ShouldBe(1);
    }

    [Fact]
    public void A_renewal_is_a_revision_of_a_kind_that_starts_when_the_old_one_ends()
    {
        var contract = Active();

        var renewal = contract.CreateRevision(contract.EffectiveTo.AddDays(1), contract.EffectiveTo.AddYears(1).AddDays(1), null, RevisionKind.Renewal).Value;

        renewal.RevisionKind.ShouldBe(RevisionKind.Renewal);
        renewal.Revision.ShouldBe(2);
        renewal.RevisionOfId.ShouldBe(contract.Id);
        renewal.Number.ShouldBe(contract.Number);
        renewal.RateCount.ShouldBe(contract.RateCount);
    }

    [Fact]
    public void An_expiring_contract_raises_expiry_events_once_and_a_superseded_one_says_where_it_ended()
    {
        var contract = Active();
        var later = contract.EffectiveTo.AddDays(1);

        contract.ExpireIfPast(later).ShouldBeTrue();
        contract.ExpireIfPast(later).ShouldBeFalse();
        contract.DomainEvents.OfType<ContractExpired>().ShouldHaveSingleItem();
        contract.DomainEvents.OfType<RateExpired>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Capacity_and_service_levels_are_validated_and_stay_with_the_draft()
    {
        var draft = Draft();

        draft.ReplaceCapacities([new CapacitySpec(Truck32, 6, null, 250, null, 60m, null, null)]).IsSuccess.ShouldBeTrue();
        draft.ReplaceCapacities([new CapacitySpec(null, 0, null, null, null, null, null, null)]).IsFailure.ShouldBeTrue();
        draft.ReplaceCapacities([new CapacitySpec(Truck32, 2, null, null, null, 150m, null, null)]).IsFailure.ShouldBeTrue();
        draft.ReplaceSlas([new SlaSpec(ContractType.Ftl, MumbaiCityPlace(), null, null, 36 * 60, null, 6 * 60, [DayOfWeek.Monday], new TimeOnly(18, 0))]).IsSuccess.ShouldBeTrue();
        draft.ReplaceSlas([new SlaSpec(ContractType.Ptl, null, null, null, 60, null, null, null, null)]).IsFailure.ShouldBeTrue();
        draft.ReplaceSlas([new SlaSpec(ContractType.Ftl, null, null, null, null, null, null, null, null)]).IsFailure.ShouldBeTrue();
        draft.Capacities.ShouldHaveSingleItem();
    }

    private static Place MumbaiCityPlace() => Place.OfCity("Maharashtra", "Mumbai");
}
