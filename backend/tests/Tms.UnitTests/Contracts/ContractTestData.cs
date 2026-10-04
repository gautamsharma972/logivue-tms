using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.UnitTests.Contracts;

internal static class ContractTestData
{
    public static readonly Guid Tenant = Guid.NewGuid();
    public static readonly Guid Transporter = Guid.NewGuid();
    public static readonly Guid Truck32 = Guid.NewGuid();
    public static readonly Guid Truck14 = Guid.NewGuid();
    public static readonly DateOnly Today = new(2026, 6, 15);
    public static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    public static Location Pune => new("Maharashtra", "Pune");

    public static Location Mumbai => new("maharashtra", "  mumbai ");

    public static Location Surat => new("Gujarat", "Surat");

    public static Location Delhi => new("Delhi", "New Delhi");

    public static Zone? NoZones(string code) => null;

    public static Func<string, Zone?> Zones(params Zone[] zones) => code => zones.FirstOrDefault(z => z.Code == code);

    public static WeightSlabPricing Slabs(SlabMode mode = SlabMode.Whole, decimal minCharge = 0, decimal minKg = 0) =>
        new(mode, [new WeightSlab(0, 100, 10m), new WeightSlab(100, 500, 8m), new WeightSlab(500, null, 6m)], minCharge, minKg);

    public static ContractTerms Terms(
        decimal volumetric = 250m, decimal loading = 0, decimal unloading = 0, decimal multiDrop = 0, decimal minConsignment = 0) =>
        new(volumetric, 24m, 100m, loading, unloading, multiDrop, minConsignment, null);

    public static FuelClause Fuel(
        FuelStepUnit unit = FuelStepUnit.Rupees, decimal basePrice = 90m, decimal step = 1m, decimal impact = 0.5m,
        decimal deadBand = 0m, decimal? cap = null, FuelDirection direction = FuelDirection.Both) =>
        new("Delhi", basePrice, unit, step, impact, deadBand, cap, direction);

    public static RateCardSpec Spec(Place origin, Place destination, Pricing pricing, Guid? vehicle = null, bool bothWays = false, decimal? minKm = null, decimal? maxKm = null) =>
        new(origin, destination, bothWays, vehicle, minKm, maxKm, pricing);

    public static Contract Draft(ContractType type = ContractType.Ftl, FuelClause? fuel = null, ContractTerms? terms = null)
    {
        var from = Today.AddDays(-10);
        return Contract.Create(Tenant, "CN-00001", Transporter, type, "Test contract", from, from.AddYears(1), 30, 5_000_000m, Guid.NewGuid(), terms ?? Terms(), fuel).Value;
    }

    /// <summary>An approved, in-force contract holding the given rates.</summary>
    public static Contract Active(ContractType type, IEnumerable<RateCardSpec> rates, FuelClause? fuel = null, ContractTerms? terms = null)
    {
        var contract = Draft(type, fuel, terms);
        contract.ReplaceRates(rates.ToList()).IsSuccess.ShouldBeTrue();
        contract.MarkSubmitted(Guid.NewGuid(), ApprovalStatus.Approved, Now).IsSuccess.ShouldBeTrue();
        return contract;
    }

    public static FreightQuery Query(Location? origin = null, Location? destination = null, Guid? vehicle = null, decimal? weight = null,
        decimal? volume = null, decimal? km = null, int drops = 1, ContractType? type = null) =>
        new(Today, origin ?? Pune, destination ?? Mumbai, vehicle, type, weight, volume, km, drops);
}
