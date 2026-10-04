using System.Net;
using Tms.Modules.Approvals.Application;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Domain;

namespace Tms.IntegrationTests.Infrastructure;

internal static class ContractApiData
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));

    public static readonly ContractTerms DefaultTerms = new(250m, 24m, 100m, 0m, 0m, 0m, 0m, null);

    /// <summary>A transporter that has been through onboarding and is Active (approval is bypassed by a threshold nobody reaches).</summary>
    public static async Task<TransporterDto> ActiveTransporterAsync(HttpClient admin)
    {
        await TransporterTestData.SetOnboardingPolicyAsync(admin, new PolicyStepDto("Only for huge vendors", TransporterPermissions.Approve, 1_000_000_000m));
        var ready = await TransporterTestData.CreateReadyAsync(admin);
        var submitted = await admin.PostAsync($"/api/v1/transporters/{ready.Id}/submit", null);
        var active = await submitted.ReadAsync<TransporterDto>();
        active.Status.ShouldBe(TransporterStatus.Active);
        return active;
    }

    /// <summary>Contracts need no human approval (no step applies to their estimated spend).</summary>
    public static Task NoApprovalNeededAsync(HttpClient admin) =>
        SetContractPolicyAsync(admin, new PolicyStepDto("Only for huge contracts", ContractPermissions.Approve, 1_000_000_000_000m));

    public static Task OneApproverAsync(HttpClient admin) =>
        SetContractPolicyAsync(admin, new PolicyStepDto("Commercial head", ContractPermissions.Approve, null));

    private static async Task SetContractPolicyAsync(HttpClient admin, params PolicyStepDto[] steps)
    {
        var all = await (await admin.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<PolicyDto>>();
        var version = all.Single(p => p.DocumentType == "freight_contract").Version;
        var response = await admin.PutJsonAsync("/api/v1/approvals/policies/freight_contract", new SavePolicyRequest(true, steps, version));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    public static async Task<VehicleTypeDto> VehicleTypeAsync(HttpClient client, string code = "TRUCK_32FT_MXL") =>
        (await (await client.GetAsync("/api/v1/vehicle-types")).ReadAsync<List<VehicleTypeDto>>()).First(t => t.Code == code);

    public static SaveContractRequest NewContract(
        Guid transporterId, ContractType type = ContractType.Ftl, FuelClause? fuel = null, decimal? spend = 5_000_000m,
        DateOnly? from = null, DateOnly? to = null, ContractTerms? terms = null) =>
        new(transporterId, type, $"{type} contract {Guid.NewGuid().ToString("N")[..6]}", from ?? Today.AddDays(-5), to ?? Today.AddYears(1), 30, spend, null,
            terms ?? DefaultTerms, fuel, null);

    public static PlaceDto City(string state, string city) => new(PlaceKind.City, state, city, null);

    public static PlaceDto State(string state) => new(PlaceKind.State, state, null, null);

    public static PlaceDto Zone(string code) => new(PlaceKind.Zone, null, null, code);

    public static readonly PlaceDto Anywhere = new(PlaceKind.Any, null, null, null);

    public static RateInputDto Flat(PlaceDto from, PlaceDto to, decimal amount, Guid vehicleTypeId, bool bothWays = false) =>
        new(from, to, bothWays, vehicleTypeId, null, null, new FlatTripPricing(amount));

    public static RateInputDto Slabbed(PlaceDto from, PlaceDto to, params (decimal From, decimal? To, decimal Rate)[] slabs) =>
        new(from, to, false, null, null, null,
            new WeightSlabPricing(SlabMode.Whole, slabs.Select(s => new WeightSlab(s.From, s.To, s.Rate)).ToList(), 0, 0));

    public static async Task<ContractDto> CreateAsync(HttpClient admin, SaveContractRequest request)
    {
        var response = await admin.PostJsonAsync("/api/v1/contracts", request);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ContractDto>();
    }

    public static async Task<ContractDto> WithRatesAsync(HttpClient admin, ContractDto contract, params RateInputDto[] rates)
    {
        var response = await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/rates", new SaveRatesRequest(rates, contract.Version));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ContractDto>();
    }

    public static async Task<ContractDto> GetAsync(HttpClient client, Guid id) =>
        await (await client.GetAsync($"/api/v1/contracts/{id}")).ReadAsync<ContractDto>();

    public static async Task<ContractDto> SubmitAsync(HttpClient client, Guid id)
    {
        var response = await client.PostAsync($"/api/v1/contracts/{id}/submit", null);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ContractDto>();
    }

    /// <summary>A contract with rates that is already Active.</summary>
    public static async Task<ContractDto> ActiveContractAsync(HttpClient admin, Guid transporterId, ContractType type, SaveContractRequest? request = null, params RateInputDto[] rates)
    {
        await NoApprovalNeededAsync(admin);
        var draft = await CreateAsync(admin, request ?? NewContract(transporterId, type));
        var withRates = await WithRatesAsync(admin, draft, rates);
        var active = await SubmitAsync(admin, withRates.Summary.Id);
        active.Summary.Status.ShouldBe(ContractStatus.Active);
        return active;
    }

    public static QuoteRequest Quote(string fromState, string fromCity, string toState, string toCity, Guid? vehicle = null, decimal? weight = null,
        decimal? volume = null, decimal? km = null, int drops = 1, DateOnly? date = null, Guid? contractId = null, bool preview = false, ContractType? type = null) =>
        new(date, new Location(fromState, fromCity), new Location(toState, toCity), vehicle, type, weight, volume, km, drops, contractId, preview);

    public static async Task<QuoteResultDto> QuoteAsync(HttpClient client, QuoteRequest request)
    {
        var response = await client.PostJsonAsync("/api/v1/freight/quote", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<QuoteResultDto>();
    }
}
