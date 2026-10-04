using Tms.Modules.Contracts.Domain;
using static Tms.UnitTests.Contracts.ContractTestData;

namespace Tms.UnitTests.Contracts;

public class RateSelectorTests
{
    private static readonly Pricing P100 = new FlatTripPricing(100m);
    private static readonly Pricing P200 = new FlatTripPricing(200m);
    private static readonly Pricing P300 = new FlatTripPricing(300m);

    private static decimal? PickedAmount(Contract contract, FreightQuery query, params Zone[] zones)
    {
        var picked = RateSelector.BestPerContract([contract], query, Zones(zones));
        return picked.Count == 0 ? null : ((FlatTripPricing)picked[0].Card.Pricing).AmountPerTrip;
    }

    [Fact]
    public void The_most_specific_lane_wins()
    {
        var contract = Active(ContractType.Ftl,
        [
            Spec(Place.Anywhere, Place.Anywhere, P100, Truck32, minKm: 0, maxKm: 5000),
            Spec(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), P200, Truck32),
            Spec(Place.OfCity("Maharashtra", "Pune"), Place.OfCity("Maharashtra", "Mumbai"), P300, Truck32),
        ]);

        PickedAmount(contract, Query(vehicle: Truck32, km: 150m)).ShouldBe(300m, "city to city beats state to state beats anywhere");
        PickedAmount(contract, Query(Pune, new Location("Maharashtra", "Nagpur"), Truck32, km: 700m)).ShouldBe(200m);
        PickedAmount(contract, Query(Pune, Delhi, Truck32, km: 1400m)).ShouldBe(100m);
    }

    [Fact]
    public void A_zone_sits_between_state_and_city_in_specificity()
    {
        var west = Zone.Create(Tenant, "WEST", "West", [new ZoneMember("Maharashtra", null), new ZoneMember("Gujarat", null)]).Value;
        var contract = Active(ContractType.Ftl,
        [
            Spec(Place.OfState("Maharashtra"), Place.OfState("Gujarat"), P100, Truck32),
            Spec(Place.OfZone("WEST"), Place.OfZone("WEST"), P200, Truck32),
        ]);

        PickedAmount(contract, Query(Pune, Surat, Truck32), west).ShouldBe(200m, "zone→zone (2+2) outranks state→state (1+1)");
        PickedAmount(contract, Query(Pune, Surat, Truck32)).ShouldBe(100m, "without the zone defined only the state rate matches");
    }

    [Fact]
    public void A_vehicle_specific_rate_beats_a_generic_one_for_the_same_lane()
    {
        var contract = Active(ContractType.Ftl,
        [
            Spec(Place.OfState("Maharashtra"), Place.OfState("Gujarat"), P100, Truck14),
            Spec(Place.OfState("Maharashtra"), Place.OfState("Gujarat"), P200, Truck32),
        ]);

        PickedAmount(contract, Query(Pune, Surat, Truck32)).ShouldBe(200m);
        PickedAmount(contract, Query(Pune, Surat, Truck14)).ShouldBe(100m);
        PickedAmount(contract, Query(Pune, Surat, Guid.NewGuid())).ShouldBeNull("no rate for that vehicle type");
        PickedAmount(contract, Query(Pune, Surat, vehicle: null)).ShouldBeNull("a vehicle-specific rate needs the vehicle");
    }

    [Fact]
    public void Distance_bands_choose_between_rates_and_need_a_distance()
    {
        var contract = Active(ContractType.Ftl,
        [
            Spec(Place.Anywhere, Place.Anywhere, P100, Truck32, minKm: 0, maxKm: 250),
            Spec(Place.Anywhere, Place.Anywhere, P200, Truck32, minKm: 251, maxKm: 750),
            Spec(Place.Anywhere, Place.Anywhere, P300, Truck32, minKm: 751, maxKm: null),
        ]);

        PickedAmount(contract, Query(vehicle: Truck32, km: 250m)).ShouldBe(100m, "upper bound inclusive");
        PickedAmount(contract, Query(vehicle: Truck32, km: 251m)).ShouldBe(200m);
        PickedAmount(contract, Query(vehicle: Truck32, km: 5000m)).ShouldBe(300m, "open-ended band");
        PickedAmount(contract, Query(vehicle: Truck32)).ShouldBeNull("band rates cannot be chosen without a distance");
    }

    [Fact]
    public void Both_ways_rates_apply_in_reverse_but_one_way_rates_do_not()
    {
        var oneWay = Active(ContractType.Ftl, [Spec(Place.OfState("Maharashtra"), Place.OfState("Gujarat"), P100, Truck32)]);
        var bothWays = Active(ContractType.Ftl, [Spec(Place.OfState("Maharashtra"), Place.OfState("Gujarat"), P100, Truck32, bothWays: true)]);

        PickedAmount(oneWay, Query(Surat, Pune, Truck32)).ShouldBeNull();
        PickedAmount(bothWays, Query(Surat, Pune, Truck32)).ShouldBe(100m);
    }

    [Fact]
    public void Contracts_outside_their_dates_or_not_active_are_never_selected()
    {
        var rates = new[] { Spec(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), P100, Truck32) };
        var draft = Draft(); draft.ReplaceRates(rates);
        var active = Active(ContractType.Ftl, rates);
        var terminated = Active(ContractType.Ftl, rates); terminated.Terminate("ended", Today);
        var q = Query(vehicle: Truck32);

        PickedAmount(draft, q).ShouldBeNull("drafts are not in force");
        PickedAmount(active, q).ShouldBe(100m);
        PickedAmount(active, q with { Date = Today.AddYears(2) }).ShouldBeNull("after the end date");
        PickedAmount(active, q with { Date = Today.AddYears(-1) }).ShouldBeNull("before the start date");
        PickedAmount(terminated, q).ShouldBe(100m, "terminated today: still valid on its last day");
        PickedAmount(terminated, q with { Date = Today.AddDays(-5) }).ShouldBe(100m, "history stays priceable for bill audit");
        PickedAmount(terminated, q with { Date = Today.AddDays(1) }).ShouldBeNull();
    }

    [Fact]
    public void The_contract_type_filter_is_honoured()
    {
        var ftl = Active(ContractType.Ftl, [Spec(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), P100, Truck32)]);

        RateSelector.BestPerContract([ftl], Query(vehicle: Truck32, type: ContractType.Ptl), NoZones).ShouldBeEmpty();
        RateSelector.BestPerContract([ftl], Query(vehicle: Truck32, type: ContractType.Ftl), NoZones).Count.ShouldBe(1);
    }

    [Fact]
    public void Each_contract_contributes_at_most_one_rate_so_transporters_can_be_compared()
    {
        var a = Active(ContractType.Ftl, [Spec(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), P200, Truck32)]);
        var b = Active(ContractType.Ftl, [Spec(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), P100, Truck32), Spec(Place.Anywhere, Place.Anywhere, P300, Truck32, minKm: 0, maxKm: 9999)]);

        var picked = RateSelector.BestPerContract([a, b], Query(vehicle: Truck32, km: 100m), NoZones);

        picked.Count.ShouldBe(2);
        picked.Select(p => ((FlatTripPricing)p.Card.Pricing).AmountPerTrip).Order().ShouldBe([100m, 200m]);
    }
}
