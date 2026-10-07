using System.Text;
using Microsoft.EntityFrameworkCore;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Application.Rating;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Contracts.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Contracts.Integration;

internal sealed class ContractsDemoSeeder(
    ContractsDbContext db, ICurrentUser user, IVehicleTypeDirectory vehicleTypes, IFileStore files, RatingCandidateLoader loader, TimeProvider clock) : IContractsDemoSeeder
{
    private static readonly (string Name, string State, string City, string Zone, double Lat)[] Cities =
    [
        ("Mumbai", "Maharashtra", "Mumbai", "WEST", 0), ("Pune", "Maharashtra", "Pune", "WEST", 0), ("Surat", "Gujarat", "Surat", "WEST", 0), ("Ahmedabad", "Gujarat", "Ahmedabad", "WEST", 0),
        ("Delhi", "Delhi", "New Delhi", "NORTH", 0), ("Jaipur", "Rajasthan", "Jaipur", "NORTH", 0), ("Chandigarh", "Chandigarh", "Chandigarh", "NORTH", 0), ("Lucknow", "Uttar Pradesh", "Lucknow", "NORTH", 0),
        ("Bengaluru", "Karnataka", "Bengaluru", "SOUTH", 0), ("Chennai", "Tamil Nadu", "Chennai", "SOUTH", 0), ("Hyderabad", "Telangana", "Hyderabad", "SOUTH", 0), ("Vijayawada", "Andhra Pradesh", "Vijayawada", "SOUTH", 0),
        ("Kolkata", "West Bengal", "Kolkata", "EAST", 0), ("Patna", "Bihar", "Patna", "EAST", 0),
    ];

    private static readonly (string From, string To, int Km)[] Lanes =
    [
        ("Mumbai", "Pune", 150), ("Mumbai", "Surat", 280), ("Mumbai", "Ahmedabad", 530), ("Pune", "Bengaluru", 840), ("Delhi", "Jaipur", 280), ("Delhi", "Chandigarh", 250), ("Delhi", "Lucknow", 530),
        ("Bengaluru", "Chennai", 350), ("Chennai", "Hyderabad", 630), ("Hyderabad", "Vijayawada", 270), ("Kolkata", "Patna", 590), ("Ahmedabad", "Jaipur", 660),
    ];

    private static readonly byte[] TinyPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\n2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj\n3 0 obj<</Type/Page/Parent 2 0 R/MediaBox[0 0 200 200]>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF");

    public async Task<ContractsDemoResult> SeedAsync(ContractsDemoRequest request, CancellationToken cancellationToken)
    {
        if (user.TenantId is not { } tenant)
        {
            throw new InvalidOperationException("The demo needs a signed-in tenant.");
        }

        if (request.Carriers.Count == 0)
        {
            return new ContractsDemoResult(0, 0, 0, 0, 0, "Give at least one carrier to put the demo contracts with.");
        }

        if (await db.Contracts.AnyAsync(c => c.Number == "CNT-ABC-2026", cancellationToken))
        {
            return new ContractsDemoResult(0, 0, 0, 0, 0, "The demo contracts are already there.");
        }

        var now = clock.GetUtcNow();
        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromMinutes(330)).DateTime);
        var types = (await vehicleTypes.ListActiveAsync(cancellationToken)).OrderByDescending(t => t.PayloadKg).ToList();
        var big = types.FirstOrDefault(t => t.Code == "TRUCK_32FT_MXL") ?? types[0];
        var mid = types.FirstOrDefault(t => t.Id != big.Id) ?? big;
        var small = types.FirstOrDefault(t => t.Id != big.Id && t.Id != mid.Id) ?? mid;

        await EnsureMastersAsync(tenant, today, cancellationToken);
        var contracts = new List<Contract>();
        Guid Carrier(int i) => request.Carriers[i % request.Carriers.Count].Id;

        // ---- the worked example: three versions of one contract
        var abc = await BuildAbcAsync(tenant, Carrier(0), big.Id, today, now, contracts, cancellationToken);

        // ---- the rest of the book
        var rng = new Random(2026);
        for (var i = 1; i <= 12; i++)
        {
            var contract = NewContract(tenant, $"CNT-FTL-{i:00}", Carrier(i), ContractType.Ftl, $"FTL lanes, carrier {i}", today.AddDays(-120 + (i * 7)), today.AddDays(i switch { 3 => 25, 7 => 12, 9 => 40, _ => 200 + (i * 10) }));
            contract.ApplyExtras(new ContractExtras("INR", i % 2 == 0 ? "Retail" : "Industrial", "Contract desk", 60, i % 4 == 0, [ContractType.Ptl])).Must();
            var region = i % 3 == 0 ? "Chennai" : i % 2 == 0 ? "Delhi" : "Mumbai";
            contract.ReplaceDphRules([DphFor(i, region, today)]).Must();
            contract.ReplaceRates(FtlRates(i, big.Id, mid.Id, small.Id, rng)).Must();
            contract.ReplaceAccessorials(Charges(today)).Must();
            contract.ReplaceCapacities([new CapacitySpec(big.Id, 2 + (i % 5), null, 40 + (i * 10), null, 15 + (i * 3), null, null), new CapacitySpec(mid.Id, 1 + (i % 3), null, 20, null, null, null, null)]).Must();
            contract.ReplaceSlas([new SlaSpec(ContractType.Ftl, null, null, 120, 36 * 60, 240, 6 * 60, [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday], new TimeOnly(18, 0))]).Must();
            Activate(contract, now);
            contracts.Add(contract);
        }

        var ptl = NewContract(tenant, "CNT-PTL-01", Carrier(2), ContractType.Ptl, "Part-load network", today.AddDays(-90), today.AddDays(270));
        ptl.ReplaceRates(PtlRates()).Must();
        Activate(ptl, now);
        contracts.Add(ptl);

        var dedicated = NewContract(tenant, "CNT-DED-01", Carrier(3), ContractType.Dedicated, "Dedicated 32 ft, Mumbai–Pune shuttle", today.AddDays(-60), today.AddDays(300));
        dedicated.ReplaceRates([new RateCardSpec(Place.OfState("Maharashtra"), Place.OfState("Maharashtra"), false, big.Id, null, null, new DedicatedPricing(185_000m, 6_000m, 32m, 0m, 0m))]).Must();
        Activate(dedicated, now);
        contracts.Add(dedicated);

        // an expired one, a suspended one, and a contract that ended and was renewed
        var expired = NewContract(tenant, "CNT-OLD-01", Carrier(4), ContractType.Ftl, "Last year's contract", today.AddDays(-400), today.AddDays(-35));
        expired.ReplaceRates(FtlRates(1, big.Id, mid.Id, small.Id, rng).Take(6).ToList()).Must();
        Activate(expired, now);
        expired.ExpireIfPast(today);
        contracts.Add(expired);

        var suspended = NewContract(tenant, "CNT-SUSP-01", Carrier(5), ContractType.Ftl, "Under dispute", today.AddDays(-100), today.AddDays(250));
        suspended.ReplaceRates(FtlRates(2, big.Id, mid.Id, small.Id, rng).Take(6).ToList()).Must();
        Activate(suspended, now);
        suspended.Suspend("Invoice dispute under review", today.AddDays(-3));
        contracts.Add(suspended);

        // drafts that show what validation catches, and the fallback and DPH demonstrations
        contracts.Add(ConflictDraft(tenant, Carrier(6), big.Id, today));
        contracts.Add(FallbackDemo(tenant, Carrier(7), big.Id, today, now));
        contracts.Add(DphDemo(tenant, Carrier(8), big.Id, today, now));
        var drafting = NewContract(tenant, "CNT-DRAFT-01", Carrier(9), ContractType.Ftl, "New carrier, being negotiated", today.AddDays(30), today.AddDays(395));
        drafting.ReplaceRates(FtlRates(3, big.Id, mid.Id, small.Id, rng).Take(8).ToList()).Must();
        contracts.Add(drafting);

        foreach (var contract in contracts.Where(c => db.Entry(c).State == EntityState.Detached))
        {
            db.Contracts.Add(contract);
        }

        await db.SaveChangesAsync(cancellationToken);

        // documents on a few contracts
        var documents = 0;
        foreach (var contract in new[] { abc[2], contracts[3], contracts[4], ptl, dedicated })
        {
            documents += await AttachAsync(tenant, contract, today, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        var ratings = await SeedRatingsAsync(tenant, abc, today, now, big.Id, cancellationToken);
        var allContracts = await db.Contracts.AsNoTracking().CountAsync(cancellationToken);
        var rates = await db.RateCards.AsNoTracking().CountAsync(cancellationToken);
        var rules = await db.DphRules.AsNoTracking().CountAsync(cancellationToken);
        return new ContractsDemoResult(allContracts, rates, rules, ratings, documents, $"Seeded {allContracts} contract revisions with {rates} rates, {rules} DPH rule versions and {ratings} ratings.");
    }

    // ---- masters

    private async Task EnsureMastersAsync(Guid tenant, DateOnly today, CancellationToken cancellationToken)
    {
        var zones = new Dictionary<string, string[]>
        {
            ["WEST"] = ["Maharashtra", "Gujarat", "Goa"], ["NORTH"] = ["Delhi", "Haryana", "Punjab", "Uttar Pradesh", "Rajasthan", "Chandigarh"],
            ["SOUTH"] = ["Karnataka", "Tamil Nadu", "Andhra Pradesh", "Telangana", "Kerala"], ["EAST"] = ["West Bengal", "Odisha", "Bihar", "Jharkhand"],
        };
        var have = (await db.Zones.AsNoTracking().Select(z => z.Code).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var (code, states) in zones.Where(z => !have.Contains(z.Key)))
        {
            db.Zones.Add(Zone.Create(tenant, code, $"{code[0]}{code[1..].ToLowerInvariant()} India", states.Select(s => new ZoneMember(s, null))).Value);
        }

        if (!await db.AccessorialTypes.AnyAsync(cancellationToken))
        {
            foreach (var (code, name, calc, unit) in AccessorialType.Standard)
            {
                db.AccessorialTypes.Add(AccessorialType.Create(tenant, code, name, null, calc, unit).Value);
            }
        }

        // a monthly diesel index for four regions, January to this month
        var existing = (await db.DieselPrices.AsNoTracking().Select(d => new { d.Region, d.EffectiveFrom }).ToListAsync(cancellationToken)).Select(d => (d.Region, d.EffectiveFrom)).ToHashSet();
        var baseline = new Dictionary<string, decimal> { ["Mumbai"] = 90m, ["Delhi"] = 87m, ["Chennai"] = 93m, ["Kolkata"] = 92m };
        foreach (var (region, price) in baseline)
        {
            for (var m = 0; m < 10; m++)
            {
                var from = new DateOnly(today.Year, 1, 1).AddMonths(m);
                if (from > today || existing.Contains((region.ToUpperInvariant(), from)))
                {
                    continue;
                }

                var wobble = new[] { 0m, 1.2m, 2.5m, 4.1m, 5.3m, 6.8m, 12m, 12.2m, 11m, 12m }[m];
                db.DieselPrices.Add(DieselPrice.Create(tenant, region, from, price + wobble, "Demo index").Value);
            }
        }

        // the DPH demonstration reads its own index: base ₹90, now ₹99
        if (!existing.Contains(("DEMO DIESEL", today.AddDays(-7))))
        {
            db.DieselPrices.Add(DieselPrice.Create(tenant, "Demo diesel", today.AddDays(-7), 99m, "Demo index").Value);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    // ---- CNT-ABC-2026: three versions

    private async Task<List<Contract>> BuildAbcAsync(Guid tenant, Guid carrier, Guid truck, DateOnly today, DateTimeOffset now, List<Contract> all, CancellationToken cancellationToken)
    {
        var mumbai = Place.OfCity("Maharashtra", "Mumbai");
        var pune = Place.OfCity("Maharashtra", "Pune");
        var dphRegion = "Mumbai";
        DphRuleSpec Monthly(string month, DateOnly from, DateOnly to, decimal basePrice) =>
            new("DPH", $"Monthly diesel adjustment, {month}", DphFormula.PercentageVariation, dphRegion, basePrice, from, 30m, 0m, EffectiveFrom: from, EffectiveTo: to, IsDefault: true, Frequency: DphFrequency.Monthly);
        RateCardSpec Rate(decimal amount, int priority = 10) =>
            new(mumbai, pune, false, truck, 150m, 200m, new FlatTripPricing(amount), new RateExtras("RATE-MUM-PUN-32FT", priority, null, null, 10_000m, 15_000m));
        RateCardSpec Surat(decimal amount) => new(mumbai, Place.OfCity("Gujarat", "Surat"), false, truck, 250m, 320m, new FlatTripPricing(amount), new RateExtras("RATE-MUM-SUR-32FT", 100, null, null, 10_000m, 15_000m));

        Contract Version(int revision, string title, DateOnly from, DateOnly to, Contract? previous, IReadOnlyList<DphRuleSpec> dph, decimal mumPune, int rateStep)
        {
            var contract = previous is null ? NewContract(tenant, "CNT-ABC-2026", carrier, ContractType.Ftl, title, from, to) : previous.CreateRevision(from, to, null, RevisionKind.Amendment).Value;
            contract.ReplaceDphRules(dph).Must();
            // rate versions: this rate has been revised many times already; each version here moves it on by one
            var before = new Dictionary<string, RateCard> { ["RATE-MUM-PUN-32FT"] = RateCard.Create(tenant, contract.Id, Rate(mumPune - 500), rateStep - 1), ["RATE-MUM-SUR-32FT"] = RateCard.Create(tenant, contract.Id, Surat(52_000m), 1) };
            contract.ReplaceRates([Rate(mumPune), Surat(52_000m)], before).Must();
            return contract;
        }

        var q2 = new DateOnly(2026, 4, 1);
        var v1 = Version(1, "ABC Logistics FTL, term 1", q2, new DateOnly(2026, 6, 30), null, [Monthly("April", q2, new DateOnly(2026, 4, 30), 90m), Monthly("May", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 90m), Monthly("June", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), 90m)], 37_000m, 6);
        v1.ReplaceAccessorials(Charges(q2)).Must();
        Activate(v1, now);

        // version 2 (July to September): the rate is dearer and DPH is repriced monthly, so 10 July is in DPH version 4
        var v2Dph = new[]
        {
            Monthly("April", new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), 90m), Monthly("May", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31), 90m), Monthly("June", new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 30), 90m),
            Monthly("July", new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31), 90m), Monthly("August", new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31), 90m), Monthly("September", new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), 90m),
        };
        var v2 = Version(2, "ABC Logistics FTL, term 2", new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), v1, v2Dph, 38_000m, 7);
        v2.ReplaceAccessorials([new AccessorialSpec("LOADING", "Loading", AccessorialCalc.Fixed, "TRIP", 280m, AutoApply: true), .. Charges(q2)]).Must();
        Activate(v2, now);
        v1.SupersedeBy(v2.EffectiveFrom);

        // version 3 (October on): the rate rises, and the worked example is rated here
        var v3Dph = new[] { Monthly("October", new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), 90m), Monthly("November", new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), 90m), Monthly("December", new DateOnly(2026, 12, 1), new DateOnly(2027, 12, 31), 90m) };
        var v3 = Version(3, "ABC Logistics FTL, term 3", new DateOnly(2026, 10, 1), new DateOnly(2027, 9, 30), v2, v3Dph, 38_000m, 8);
        v3.ReplaceDphRules([new DphRuleSpec("DPH", "Diesel adjustment", DphFormula.PercentageVariation, dphRegion, 90m, new DateOnly(2026, 10, 1), 30m, 0m, IsDefault: true)]).Must();
        v3.ReplaceAccessorials([new AccessorialSpec("TOLL", "Toll", AccessorialCalc.Reimbursed, "TRIP"), new AccessorialSpec("DETENTION", "Detention", AccessorialCalc.Tiered, "HOUR",
            Tiers: [new AccessorialTier(0, 2, 0), new AccessorialTier(2, 5, 500), new AccessorialTier(5, null, 750)]), new AccessorialSpec("ADDITIONAL_STOP", "Additional stop", AccessorialCalc.PerUnit, "STOP", 750m, IncludedQuantity: 2)]).Must();
        v3.ReplaceCapacities([new CapacitySpec(truck, 6, 18_000m, 250, null, 60m, null, null)]).Must();
        v3.ReplaceSlas([new SlaSpec(ContractType.Ftl, mumbai, pune, 120, 36 * 60, 240, 6 * 60, [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], new TimeOnly(18, 0))]).Must();
        Activate(v3, now);
        v2.SupersedeBy(v3.EffectiveFrom);

        all.AddRange([v1, v2, v3]);
        foreach (var c in new[] { v1, v2, v3 })
        {
            db.Contracts.Add(c);
        }

        await db.SaveChangesAsync(cancellationToken);
        return [v1, v2, v3];
    }

    private async Task<int> SeedRatingsAsync(Guid tenant, List<Contract> abc, DateOnly today, DateTimeOffset now, Guid truck, CancellationToken cancellationToken)
    {
        var number = 0;
        var made = 0;
        async Task<FreightRating> Rate(RatingInput input, DateTimeOffset at, bool commit)
        {
            var candidates = await loader.LoadAsync(input, null, preview: false, cancellationToken);
            var outcome = RatingEngine.Rate(candidates.Contracts, input, candidates.Context);
            var rating = FreightRating.Create(tenant, $"FR-{9_000 + ++number:D6}", input, outcome, commit && outcome.Qualified, at);
            db.Ratings.Add(rating);
            made++;
            return rating;
        }

        RatingInput Input(string fromCity, string toCity, string state, DateOnly date, string shipment, decimal kg, decimal km, Guid? vehicle, Dictionary<string, decimal>? inputs = null) =>
            new(date, null, new Location(state, fromCity), new Location(state, toCity), ContractType.Ftl, vehicle, kg, null, km, 1, [], inputs ?? [], shipment);

        // SH-10025, rated on 10 July 2026: contract V2, the rate's seventh version, DPH version 4, ₹39,800
        await Rate(Input("Mumbai", "Pune", "Maharashtra", new DateOnly(2026, 7, 10), "SH-10025", 12_500m, 155m, truck), new DateTimeOffset(2026, 7, 10, 9, 0, 0, TimeSpan.FromMinutes(330)), commit: true);

        // today's rating of the worked example on the current contract
        await Rate(Input("Mumbai", "Pune", "Maharashtra", today, "SH-10031", 12_500m, 155m, truck, new() { ["TOLL"] = 1_800m }), now, commit: true);

        // a month of kept ratings across the book, and a few lanes nobody has priced
        foreach (var (from, to, km) in Lanes.Take(8))
        {
            var fromCity = Cities.First(c => c.Name == from);
            var toCity = Cities.First(c => c.Name == to);
            for (var i = 0; i < 4; i++)
            {
                var date = today.AddDays(-3 - (i * 6));
                await Rate(new RatingInput(date, null, new Location(fromCity.State, fromCity.City), new Location(toCity.State, toCity.City), ContractType.Ftl, truck, 9_000m + (i * 1_500m), null, km, 1, [], new Dictionary<string, decimal>(), $"SH-2{made:0000}"), now.AddDays(-3 - (i * 6)), commit: true);
            }
        }

        foreach (var (from, to) in new[] { ("Guwahati", "Imphal"), ("Kochi", "Mangalore"), ("Indore", "Bhopal") })
        {
            await Rate(Input(from, to, "Nowhere", today, $"SH-3{made:0000}", 8_000m, 300m, truck), now.AddDays(-made), commit: false);
        }

        await db.SaveChangesAsync(cancellationToken);
        return made;
    }

    // ---- building blocks

    private static decimal Hundreds(decimal amount) => Math.Round(amount / 100m, MidpointRounding.AwayFromZero) * 100m;

    private static Contract NewContract(Guid tenant, string number, Guid carrier, ContractType type, string title, DateOnly from, DateOnly to) =>
        Contract.Create(tenant, number, carrier, type, title, from, to, 30, 6_000_000m, null, ContractTerms.Default with { MinChargePerConsignment = type == ContractType.Ptl ? 1_500m : 0m }, null).Value;

    private static void Activate(Contract contract, DateTimeOffset now) => contract.MarkSubmitted(Guid.NewGuid(), ApprovalStatus.Approved, now).Must();

    private static List<RateCardSpec> FtlRates(int seed, Guid big, Guid mid, Guid small, Random rng)
    {
        var specs = new List<RateCardSpec>();
        var chosen = Lanes.OrderBy(l => (l.From.GetHashCode() ^ seed * 31) & 0xFF).Take(6).ToList();
        foreach (var (from, to, km) in chosen)
        {
            var a = Cities.First(c => c.Name == from);
            var b = Cities.First(c => c.Name == to);
            foreach (var (vehicle, factor, label) in new[] { (big, 1.0m, "32FT"), (mid, 0.72m, "19FT") })
            {
                var amount = Hundreds((4_000m + (km * 52m)) * factor * (0.95m + (rng.Next(0, 11) / 100m)));
                specs.Add(new RateCardSpec(Place.OfCity(a.State, a.City), Place.OfCity(b.State, b.City), false, vehicle, Math.Max(0, km - 50), km + 50, new FlatTripPricing(amount),
                    new RateExtras($"RATE-{from[..3].ToUpperInvariant()}-{to[..3].ToUpperInvariant()}-{label}-{seed:00}")));
            }
        }

        // regional fall-backs by zone, a per-km rate over distance slabs, and an all-India per-km default
        foreach (var zone in new[] { "WEST", "NORTH", "SOUTH", "EAST" })
        {
            specs.Add(new RateCardSpec(Place.OfZone(zone), Place.OfZone(zone), false, big, null, null, new FlatTripPricing(Hundreds(24_000m + (seed * 300m))), new RateExtras($"RATE-ZONE-{zone}-{seed:00}", 200)));
        }

        specs.Add(new RateCardSpec(Place.OfZone("WEST"), Place.OfZone("NORTH"), false, big, null, null, new FlatTripPricing(Hundreds(78_000m + (seed * 500m))), new RateExtras($"RATE-ZONE-WN-{seed:00}", 200)));
        specs.Add(new RateCardSpec(Place.Anywhere, Place.Anywhere, false, big, 0m, 3_000m, new SlabRatePricing(ContractType.Ftl, SlabDimension.Distance, RateUnit.Km, SlabMethod.Progressive,
            [new Slab(0, 250, 60), new Slab(250, 600, 50), new Slab(600, null, 44)]), new RateExtras($"RATE-DEFAULT-{seed:00}", 900)));
        specs.Add(new RateCardSpec(Place.Anywhere, Place.Anywhere, false, small, 0m, 3_000m, new PerKmPricing(36m, 100m, 4_500m), new RateExtras($"RATE-KM-{seed:00}", 950)));
        return specs;
    }

    private static List<RateCardSpec> PtlRates()
    {
        var specs = new List<RateCardSpec>();
        foreach (var zone in new[] { "WEST", "NORTH", "SOUTH", "EAST" })
        {
            specs.Add(new RateCardSpec(Place.OfZone(zone), Place.OfZone(zone), false, null, null, null,
                new SlabRatePricing(ContractType.Ptl, SlabDimension.Weight, RateUnit.Kg, SlabMethod.Flat, [new Slab(0, 500, 10), new Slab(500, 1_000, 9), new Slab(1_000, 3_000, 8), new Slab(3_000, null, 7)], 0), new RateExtras($"PTL-{zone}-KG")));
            specs.Add(new RateCardSpec(Place.OfZone(zone), Place.OfZone(zone), false, null, null, null,
                new SlabRatePricing(ContractType.Ptl, SlabDimension.Volume, RateUnit.Cbm, SlabMethod.Flat, [new Slab(0, 10, 950), new Slab(10, 25, 850), new Slab(25, null, 780)]), new RateExtras($"PTL-{zone}-CBM", 150)));
        }

        specs.Add(new RateCardSpec(Place.OfZone("WEST"), Place.OfZone("NORTH"), false, null, null, null,
            new SlabRatePricing(ContractType.Ptl, SlabDimension.Weight, RateUnit.Kg, SlabMethod.BaseExcess, [new Slab(0, 500, 6_500, SlabRateType.Fixed), new Slab(500, 2_000, 11), new Slab(2_000, null, 9)]), new RateExtras("PTL-WN-BASE")));
        specs.Add(new RateCardSpec(Place.OfZone("SOUTH"), Place.OfZone("WEST"), false, null, null, null,
            new SlabRatePricing(ContractType.Ptl, SlabDimension.Packages, RateUnit.Box, SlabMethod.Progressive, [new Slab(0, 50, 55), new Slab(50, null, 45)]), new RateExtras("PTL-SW-BOX")));
        return specs;
    }

    private static DphRuleSpec DphFor(int i, string region, DateOnly today)
    {
        var from = today.AddDays(-150);
        return (i % 5) switch
        {
            0 => new DphRuleSpec("DPH", "Fuel adjustment (steps)", DphFormula.ThresholdSteps, region, 88m, from, 30m, 2m, StepPercent: 3m, ImpactPercentPerStep: 0.75m, CapPercent: 6m, IsDefault: true),
            1 => new DphRuleSpec("DPH", "Fuel adjustment (percentage)", DphFormula.PercentageVariation, region, 88m, from, 30m, 5m, IsDefault: true),
            2 => new DphRuleSpec("DPH", "Fuel adjustment (fixed per step)", DphFormula.FixedAdjustment, region, 88m, from, 30m, 3m, StepPercent: 5m, FixedAmountPerStep: 400m, IsDefault: true),
            3 => new DphRuleSpec("DPH", "Fuel adjustment (per km)", DphFormula.PerKmAdjustment, region, 88m, from, 30m, 3m, StepPercent: 5m, PerKmPerStep: 0.4m, IsDefault: true),
            _ => new DphRuleSpec("DPH", "Fuel adjustment (indexed)", DphFormula.Indexed, region, 88m, from, 25m, 0m, Direction: DphDirection.EscalationOnly, IsDefault: true, Frequency: DphFrequency.Monthly),
        };
    }

    private static IReadOnlyList<AccessorialSpec> Charges(DateOnly from) =>
    [
        new("TOLL", "Toll", AccessorialCalc.Reimbursed, "TRIP"),
        new("DETENTION", "Detention", AccessorialCalc.Tiered, "HOUR", Tiers: [new AccessorialTier(0, 2, 0), new AccessorialTier(2, 5, 500), new AccessorialTier(5, null, 750)]),
        new("ADDITIONAL_STOP", "Additional stop", AccessorialCalc.PerUnit, "STOP", 750m, IncludedQuantity: 2),
        new("NIGHT_HALT", "Night halt", AccessorialCalc.PerUnit, "NIGHT", 1_200m),
        new("EXTRA_KM", "Extra kilometres", AccessorialCalc.PerUnit, "KM", 28m, MinimumCharge: 300m),
    ];

    private static Contract ConflictDraft(Guid tenant, Guid carrier, Guid truck, DateOnly today)
    {
        var contract = NewContract(tenant, "CNT-DEMO-CONFLICT", carrier, ContractType.Ftl, "Demo: two rates that cannot both apply", today, today.AddYears(1));
        RateCardSpec Banded(string code, decimal amount, decimal from, decimal to) =>
            new(Place.OfCity("Maharashtra", "Mumbai"), Place.OfCity("Karnataka", "Bengaluru"), false, truck, null, null, new FlatTripPricing(amount), new RateExtras(code, 100, null, null, from, to));
        contract.ReplaceRates([Banded("RATE-MUM-BLR-A", 62_000m, 8_000m, 14_000m), Banded("RATE-MUM-BLR-B", 64_500m, 12_000m, 18_000m)]).Must();
        return contract;
    }

    /// <summary>No exact lane for Delhi → Hyderabad, but a zone rate and a default: the zone rate is used and the result says why.</summary>
    private static Contract FallbackDemo(Guid tenant, Guid carrier, Guid truck, DateOnly today, DateTimeOffset now)
    {
        var contract = NewContract(tenant, "CNT-DEMO-FALLBACK", carrier, ContractType.Ftl, "Demo: zone rate used when there is no lane rate", today.AddDays(-30), today.AddYears(1));
        contract.ReplaceRates(
        [
            new RateCardSpec(Place.OfZone("NORTH"), Place.OfZone("SOUTH"), false, truck, null, null, new FlatTripPricing(88_000m), new RateExtras("RATE-NORTH-SOUTH", 200)),
            new RateCardSpec(Place.Anywhere, Place.Anywhere, false, truck, 0m, 4_000m, new PerKmPricing(48m, 200m, 9_000m), new RateExtras("RATE-DEFAULT", 900)),
        ]).Must();
        Activate(contract, now);
        return contract;
    }

    /// <summary>Base diesel ₹90 against ₹99 today with a 30% fuel share: a 10% variation, a 3% adjustment.</summary>
    private static Contract DphDemo(Guid tenant, Guid carrier, Guid truck, DateOnly today, DateTimeOffset now)
    {
        var contract = NewContract(tenant, "CNT-DEMO-DPH", carrier, ContractType.Ftl, "Demo: diesel price adjustment", today.AddDays(-30), today.AddYears(1));
        contract.ReplaceDphRules([new DphRuleSpec("DPH", "Demo DPH", DphFormula.PercentageVariation, "Demo diesel", 90m, today.AddDays(-30), 30m, 0m, IsDefault: true)]).Must();
        contract.ReplaceRates([new RateCardSpec(Place.OfCity("Maharashtra", "Pune"), Place.OfCity("Gujarat", "Ahmedabad"), false, truck, null, null, new FlatTripPricing(60_000m), new RateExtras("RATE-PUN-AMD-DEMO"))]).Must();
        Activate(contract, now);
        return contract;
    }

    private async Task<int> AttachAsync(Guid tenant, Contract contract, DateOnly today, CancellationToken cancellationToken)
    {
        var made = 0;
        foreach (var (kind, title) in new[] { (ContractDocumentKind.MasterAgreement, "Master transport agreement"), (ContractDocumentKind.RateAnnexure, "Rate annexure"), (ContractDocumentKind.DphAnnexure, "DPH annexure") })
        {
            var id = Guid.CreateVersion7();
            var key = $"{tenant}/contracts/{contract.Id}/1/{id}.pdf";
            await files.SaveAsync(key, new MemoryStream(TinyPdf), cancellationToken);
            var document = ContractDocument.Create(tenant, contract.Id, kind, $"{title} ({contract.Number})", key, $"{title.Replace(' ', '-').ToLowerInvariant()}.pdf", "application/pdf", TinyPdf.Length).Value;
            document.Describe($"{kind.ToString().ToUpperInvariant()[..3]}-{contract.Number}", 1, contract.EffectiveFrom, contract.EffectiveFrom, kind == ContractDocumentKind.Insurance ? today.AddDays(20) : contract.EffectiveTo);
            if (kind == ContractDocumentKind.MasterAgreement)
            {
                document.Verify(null, DateTimeOffset.UtcNow);
            }

            db.Documents.Add(document);
            made++;
        }

        return made;
    }
}

internal static class SeedExtensions
{
    /// <summary>The seeder builds known-good data; a step that fails is a bug in the seeder, so it says which, loudly.</summary>
    public static void Must(this Tms.SharedKernel.Results.Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"The contracts demo data failed one of its own checks: {result.Error.Description}");
        }
    }
}
