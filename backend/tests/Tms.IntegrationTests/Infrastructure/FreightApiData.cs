using System.Net;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Domain;
using static Tms.IntegrationTests.Infrastructure.ContractApiData;

namespace Tms.IntegrationTests.Infrastructure;

internal static class FreightApiData
{
    /// <summary>A state nobody else prices, so a test's lanes are its own.</summary>
    public static string UniqueState() => $"FRSTATE{Guid.NewGuid():N}"[..20];

    public static string UniqueRegion() => $"REG{Guid.NewGuid():N}"[..10];

    public static RateInputDto Rate(PlaceDto from, PlaceDto to, Pricing pricing, Guid? vehicle = null, RateExtras? extras = null, decimal? minKm = null, decimal? maxKm = null, bool bothWays = false) =>
        new(from, to, bothWays, vehicle, minKm, maxKm, pricing, extras);

    public static RatingRequestDto Rating(
        string fromState, string fromCity, string toState, string toCity, ContractType service = ContractType.Ftl, Guid? vehicle = null, decimal? kg = null, decimal? km = null, decimal? cbm = null,
        Guid? transporter = null, DateOnly? date = null, int stops = 1, string? shipment = null, bool commit = false, Dictionary<string, decimal>? inputs = null, string[]? capabilities = null) =>
        new(date, new Location(fromState, fromCity), new Location(toState, toCity), service, transporter, vehicle, kg, cbm, km, stops, capabilities, inputs, shipment, commit);

    public static async Task<RatingResultDto> CalculateAsync(HttpClient client, RatingRequestDto request, string route = "calculate")
    {
        var response = await client.PostJsonAsync($"/api/v1/freight-rating/{route}", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RatingResultDto>();
    }

    /// <summary>A contract through draft → DPH rules → charges → rates → approval, so it is Active.</summary>
    public static async Task<ContractDto> BuildActiveAsync(
        HttpClient admin, Guid transporterId, SaveContractRequest? request = null, DphRuleSpec[]? dph = null, AccessorialSpec[]? charges = null, RateInputDto[]? rates = null, bool approve = true)
    {
        await NoApprovalNeededAsync(admin);
        var contract = await CreateAsync(admin, request ?? NewContract(transporterId));
        if (dph is { Length: > 0 })
        {
            var saved = await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/dph-rules", new SaveDphRulesRequest(dph, contract.Version));
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            contract = await saved.ReadAsync<ContractDto>();
        }

        if (charges is { Length: > 0 })
        {
            (await admin.GetAsync("/api/v1/accessorials")).StatusCode.ShouldBe(HttpStatusCode.OK); // makes sure the standard catalogue exists
            var saved = await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/accessorials", new SaveAccessorialsRequest(charges, contract.Version));
            saved.StatusCode.ShouldBe(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            contract = await saved.ReadAsync<ContractDto>();
        }

        if (rates is { Length: > 0 })
        {
            contract = await WithRatesAsync(admin, contract, rates);
        }

        return approve ? await SubmitAsync(admin, contract.Summary.Id) : contract;
    }

    public static DphRuleSpec Dph(string region, string code = "DPH", decimal basePrice = 90m, decimal fuel = 30m, decimal threshold = 0m) =>
        new(code, "Fuel adjustment", DphFormula.PercentageVariation, region, basePrice, Today.AddDays(-60), fuel, threshold, IsDefault: true);

    public static async Task AddDieselAsync(HttpClient admin, string region, decimal price, DateOnly? from = null)
    {
        var response = await admin.PostJsonAsync("/api/v1/dph/price-index", new AddPriceIndexRequest(region, from ?? Today.AddDays(-5), price, "test"));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    public static async Task<RateValidationDto> ValidateAsync(HttpClient admin, ValidateRatesRequest request)
    {
        var response = await admin.PostJsonAsync("/api/v1/freight-rates/validate", request);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<RateValidationDto>();
    }
}
