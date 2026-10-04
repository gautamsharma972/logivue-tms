using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Domain;
using static Tms.IntegrationTests.Infrastructure.ContractApiData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ContractQuoteApiTests(TmsApiFactory factory)
{
    // Each test uses its own lane (a state nobody else prices) so quotes never pick up another test's contracts.
    private static string UniqueState() => $"TESTSTATE{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Quotes_from_several_transporters_are_returned_cheapest_first_with_a_full_breakdown()
    {
        using var admin = await factory.AdminAsync();
        var truck = await VehicleTypeAsync(admin);
        var (a, b) = (await ActiveTransporterAsync(admin), await ActiveTransporterAsync(admin));
        var state = UniqueState();
        await ActiveContractAsync(admin, a.Id, ContractType.Ftl, NewContract(a.Id, terms: DefaultTerms with { LoadingCharge = 500m }), Flat(State(state), State(state), 40_000m, truck.Id));
        await ActiveContractAsync(admin, b.Id, ContractType.Ftl, null, Flat(State(state), State(state), 36_500m, truck.Id));

        var result = await QuoteAsync(admin, Quote(state, "Town A", state, "Town B", truck.Id));

        result.Quotes.Select(q => q.Total).ShouldBe([36_500m, 40_500m]);
        result.Quotes[0].TransporterName.ShouldBe(b.LegalName);
        result.Quotes[1].Lines.Select(l => (l.Code, l.Amount)).ShouldBe([("FREIGHT", 40_000m), ("LOADING", 500m)]);
        result.Message.ShouldBeNull();
    }

    [Fact]
    public async Task Part_load_quotes_use_slabs_zones_and_volumetric_weight()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var (north, south) = (UniqueState(), UniqueState());
        var zoneCode = $"Z{Guid.NewGuid():N}"[..10];
        (await admin.PostJsonAsync("/api/v1/zones", new SaveZoneRequest(zoneCode, "Test zone", [new ZoneMember(north, null), new ZoneMember(south, "Hub City")], null)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        await ActiveContractAsync(admin, transporter.Id, ContractType.Ptl, NewContract(transporter.Id, ContractType.Ptl, terms: DefaultTerms with { VolumetricKgPerCbm = 300m }),
            Slabbed(Zone(zoneCode), Zone(zoneCode), (0, 100, 12m), (100, 500, 9m), (500, null, 7m)));

        var heavy = await QuoteAsync(admin, Quote(north, "X", south, "Hub City", weight: 200m));
        heavy.Quotes.ShouldHaveSingleItem().Total.ShouldBe(1_800m, "200 kg × ₹9 (100–500 slab)");

        var bulky = await QuoteAsync(admin, Quote(north, "X", south, "Hub City", weight: 100m, volume: 2m));
        bulky.Quotes.Single().ChargeableWeightKg.ShouldBe(600m, "2 CBM × 300 kg/CBM beats 100 kg actual");
        bulky.Quotes.Single().Total.ShouldBe(4_200m);
        bulky.Quotes.Single().Notes.ShouldContain(n => n.StartsWith("Volumetric weight"));

        var outsideZone = await QuoteAsync(admin, Quote(north, "X", south, "Some Other Town", weight: 200m));
        outsideZone.Quotes.ShouldBeEmpty("only Hub City is in the zone for the destination state");
        outsideZone.Message.ShouldNotBeNull().ShouldContain("No contract has an applicable rate");
    }

    [Fact]
    public async Task Diesel_escalation_follows_the_price_in_force_on_the_shipment_date()
    {
        using var admin = await factory.AdminAsync();
        var truck = await VehicleTypeAsync(admin);
        var transporter = await ActiveTransporterAsync(admin);
        var state = UniqueState();
        var region = $"R{Guid.NewGuid():N}"[..8];
        var clause = new FuelClause(region, 90m, FuelStepUnit.Rupees, 1m, 0.5m, 0m, 10m, FuelDirection.Both);
        await ActiveContractAsync(admin, transporter.Id, ContractType.Ftl, NewContract(transporter.Id, fuel: clause, from: Today.AddDays(-60)), Flat(State(state), State(state), 40_000m, truck.Id));
        var q = Quote(state, "A", state, "B", truck.Id);

        var before = await QuoteAsync(admin, q);
        before.Quotes.Single().Lines.ShouldHaveSingleItem();
        before.Quotes.Single().Notes.ShouldContain(n => n.Contains("No diesel price on file"));

        (await admin.PostJsonAsync("/api/v1/diesel-prices", new AddDieselPriceRequest(region, Today.AddDays(-30), 90m))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await admin.PostJsonAsync("/api/v1/diesel-prices", new AddDieselPriceRequest(region, Today.AddDays(-2), 96m))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var now = (await QuoteAsync(admin, q)).Quotes.Single();
        now.Lines.Select(l => (l.Code, l.Amount)).ShouldBe([("FREIGHT", 40_000m), ("FUEL", 1_200m)]); // ₹6 above base → 6 steps × 0.5% = 3%
        now.Total.ShouldBe(41_200m);
        now.Lines[1].Description.ShouldContain("+3%");

        var lastWeek = (await QuoteAsync(admin, q with { Date = Today.AddDays(-10) })).Quotes.Single();
        lastWeek.Lines.ShouldHaveSingleItem("the ₹96 price did not exist yet; at ₹90 there is no escalation");
        lastWeek.Total.ShouldBe(40_000m);
    }

    [Fact]
    public async Task A_draft_can_be_previewed_but_is_never_returned_by_a_normal_quote()
    {
        using var admin = await factory.AdminAsync();
        var truck = await VehicleTypeAsync(admin);
        var transporter = await ActiveTransporterAsync(admin);
        var state = UniqueState();
        var draft = await WithRatesAsync(admin, await CreateAsync(admin, NewContract(transporter.Id)), Flat(State(state), State(state), 12_345m, truck.Id));
        var q = Quote(state, "A", state, "B", truck.Id);

        (await QuoteAsync(admin, q)).Quotes.ShouldBeEmpty();
        (await QuoteAsync(admin, q with { ContractId = draft.Summary.Id })).Quotes.ShouldBeEmpty("a draft is not in force");
        (await QuoteAsync(admin, q with { ContractId = draft.Summary.Id, Preview = true })).Quotes.ShouldHaveSingleItem().Total.ShouldBe(12_345m);
    }

    [Fact]
    public async Task Quote_problems_are_explained_not_guessed()
    {
        using var admin = await factory.AdminAsync();
        var truck = await VehicleTypeAsync(admin);
        var transporter = await ActiveTransporterAsync(admin);
        var state = UniqueState();
        await ActiveContractAsync(admin, transporter.Id, ContractType.Ptl, NewContract(transporter.Id, ContractType.Ptl), Slabbed(State(state), State(state), (0, null, 10m)));

        var noWeight = await QuoteAsync(admin, Quote(state, "A", state, "B"));
        noWeight.Quotes.ShouldBeEmpty();
        noWeight.Message.ShouldNotBeNull().ShouldContain("The weight in kg is needed");

        var invalid = await admin.PostJsonAsync("/api/v1/freight/quote", Quote(state, "A", state, "B", weight: -5m));
        invalid.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await invalid.Content.ReadAsStringAsync()).ShouldContain("\"weightKg\"");
        (await admin.PostJsonAsync("/api/v1/freight/quote", Quote(state, "A", state, "B", weight: 10m, contractId: Guid.NewGuid()))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound, "an unknown contract id is an error, not an empty result");
    }

    [Fact]
    public async Task Another_organisation_never_sees_or_prices_with_our_contracts()
    {
        using var admin = await factory.AdminAsync();
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        var truck = await VehicleTypeAsync(admin);
        var transporter = await ActiveTransporterAsync(admin);
        var state = UniqueState();
        var contract = await ActiveContractAsync(admin, transporter.Id, ContractType.Ftl, null, Flat(State(state), State(state), 9_999m, truck.Id));

        (await acme.GetAsync($"/api/v1/contracts/{contract.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await acme.GetAsync($"/api/v1/contracts/{contract.Summary.Id}/rates")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await QuoteAsync(acme, Quote(state, "A", state, "B", truck.Id))).Quotes.ShouldBeEmpty();
        (await acme.PostJsonAsync("/api/v1/freight/quote", Quote(state, "A", state, "B", truck.Id, contractId: contract.Summary.Id, preview: true))).StatusCode
            .ShouldBe(HttpStatusCode.NotFound, "another organisation's contract id cannot even be probed");
        var list = await (await acme.GetAsync("/api/v1/contracts?pageSize=200")).ReadAsync<Tms.SharedKernel.Paging.PagedResult<ContractSummaryDto>>();
        list.Items.ShouldNotContain(c => c.Id == contract.Summary.Id);
    }
}
