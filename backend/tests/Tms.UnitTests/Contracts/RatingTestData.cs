using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.UnitTests.Contracts.ContractTestData;

namespace Tms.UnitTests.Contracts;

internal static class RatingTestData
{
    public static readonly Guid Other = Guid.NewGuid();

    public static RateCardSpec Rate(Place origin, Place destination, Pricing pricing, Guid? vehicle = null, RateExtras? extras = null, decimal? minKm = null, decimal? maxKm = null, bool bothWays = false) =>
        new(origin, destination, bothWays, vehicle, minKm, maxKm, pricing, extras);

    public static readonly Place PuneCity = Place.OfCity("Maharashtra", "Pune");
    public static readonly Place MumbaiCity = Place.OfCity("Maharashtra", "Mumbai");

    /// <summary>Mumbai → Pune, 32 ft truck, a fixed trip price, with whatever extras and bands the test needs.</summary>
    public static RateCardSpec MumbaiPune(decimal amount, RateExtras? extras = null, decimal? minKm = null, decimal? maxKm = null) =>
        Rate(MumbaiCity, PuneCity, new FlatTripPricing(amount), Truck32, extras, minKm, maxKm);

    public static RatingInput Input(
        Location? origin = null, Location? destination = null, ContractType service = ContractType.Ftl, Guid? vehicle = null, decimal? kg = null, decimal? cbm = null, decimal? km = null,
        int stops = 1, Guid? transporter = null, string[]? capabilities = null, Dictionary<string, decimal>? inputs = null, DateOnly? date = null) =>
        new(date ?? Today, transporter, origin ?? Mumbai, destination ?? Pune, service, vehicle, kg, cbm, km, stops, capabilities ?? [], inputs ?? [], null);

    public static RatingContext Context(Zone[]? zones = null, DieselPrice[]? prices = null, bool requireInForce = true) =>
        new(Zones(zones ?? []), prices ?? [], null, requireInForce);

    public static DieselPrice Diesel(decimal price, DateOnly from, string region = "Delhi") => DieselPrice.Create(Tenant, region, from, price).Value;

    public static Contract ActiveWith(
        IEnumerable<RateCardSpec> rates, ContractType type = ContractType.Ftl, Guid? transporter = null, IEnumerable<DphRuleSpec>? dph = null, IEnumerable<AccessorialSpec>? accessorials = null,
        ContractTerms? terms = null, DateOnly? from = null, DateOnly? to = null, string number = "CN-00001", IEnumerable<ContractType>? services = null)
    {
        var start = from ?? Today.AddDays(-30);
        var contract = Contract.Create(Tenant, number, transporter ?? Transporter, type, "Test contract", start, to ?? start.AddYears(1), 30, 5_000_000m, Guid.NewGuid(), terms ?? Terms(), null).Value;
        if (services is not null)
        {
            contract.ApplyExtras(new ContractExtras(Services: services.ToList())).IsSuccess.ShouldBeTrue();
        }

        if (dph is not null)
        {
            contract.ReplaceDphRules(dph.ToList()).IsSuccess.ShouldBeTrue();
        }

        var replaced = contract.ReplaceRates(rates.ToList());
        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Description : null);
        if (accessorials is not null)
        {
            var set = contract.ReplaceAccessorials(accessorials.ToList());
            set.IsSuccess.ShouldBeTrue(set.IsFailure ? set.Error.Description : null);
        }

        contract.MarkSubmitted(Guid.NewGuid(), ApprovalStatus.Approved, Now).IsSuccess.ShouldBeTrue();
        return contract;
    }

    public static DphRuleSpec Dph(string code = "DPH", DphFormula formula = DphFormula.PercentageVariation, decimal basePrice = 90m, decimal fuel = 30m, decimal threshold = 0m, bool isDefault = true,
        DphDirection direction = DphDirection.Both, DphFrequency frequency = DphFrequency.Shipment, decimal step = 0m, decimal impact = 0m, decimal fixedPerStep = 0m, decimal perKm = 0m, decimal? cap = null,
        DateOnly? from = null, DateOnly? to = null, bool onExcess = false) =>
        new(code, "Fuel adjustment", formula, "Delhi", basePrice, new DateOnly(2026, 4, 1), fuel, threshold, step, fixedPerStep, perKm, impact, cap, onExcess, direction, frequency, 2, from, to, isDefault);
}
