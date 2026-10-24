using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.TransportOps.Application;
using Tms.Modules.TransportOps.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

/// <summary>A customer, a CFA, two distributors, an active agreement and the clients to work them: the same setup the office does once.</summary>
internal sealed class OpsScenario : IDisposable
{
    public const string CustomerGstin = "27AABCA1234C1ZF";

    public required HttpClient Admin { get; init; }

    public required HttpClient Anonymous { get; init; }

    public required CustomerDto Customer { get; init; }

    public required LocationDto Cfa { get; init; }

    public required LocationDto Pune { get; init; }

    public required LocationDto Nashik { get; init; }

    public required AgreementDto Agreement { get; init; }

    public static DateTimeOffset Base => DateTimeOffset.UtcNow.AddDays(-2);

    public static async Task<OpsScenario> CreateAsync(TmsApiFactory factory, Action<SaveAgreementRequest, List<RateDto>>? shape = null, DetentionPolicy? detention = null, TaxSettings? tax = null, BillingRules? billing = null)
    {
        var admin = await factory.AdminAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        (await admin.PutJsonAsync("/api/v1/ops/settings", new OpsSettingsDto(new ArrivalSetting(), new NumberingSetting(), new PodSetting(), new InvoiceSetting("INV", null, "27", false), new FleetSetting()))).EnsureSuccessStatusCode();

        var customer = await Ok<CustomerDto>(admin.PostJsonAsync("/api/v1/ops/customers", new SaveCustomerRequest($"CUS{suffix}", $"Demo FMCG {suffix}", CustomerGstin, "Plot 1", "Mumbai", "Maharashtra", "400001", "Bill Desk", "bills@example.com",
            "9820012345", null, null, null, null, 30, null, null, null, "CustomerReference", ActiveStatus.Active)));
        var cfa = await Ok<LocationDto>(admin.PostJsonAsync("/api/v1/ops/locations", new SaveLocationRequest($"CFA{suffix}", "Demo CFA Mumbai", LocationKind.Cfa, customer.Id, "CFA Gate", "Mumbai", "Maharashtra", "400093", 19.0760, 72.8777, 150, null, ActiveStatus.Active)));
        var pune = await Ok<LocationDto>(admin.PostJsonAsync("/api/v1/ops/locations", new SaveLocationRequest($"PUN{suffix}", "Demo Distributor Pune", LocationKind.Distributor, customer.Id, "Chakan", "Pune", "Maharashtra", "410501", 18.7606, 73.8636, 150, null, ActiveStatus.Active)));
        var nashik = await Ok<LocationDto>(admin.PostJsonAsync("/api/v1/ops/locations", new SaveLocationRequest($"NSK{suffix}", "Demo Distributor Nashik", LocationKind.Distributor, customer.Id, "Satpur", "Nashik", "Maharashtra", "422007", 19.9975, 73.7898, 150, null, ActiveStatus.Active)));

        var rates = new List<RateDto>
        {
            new(null, cfa.Id, null, null, pune.Id, null, null, null, RateMethod.PerKg, 3.5m, [], null),
            new(null, cfa.Id, null, null, nashik.Id, null, null, null, RateMethod.PerTonne, 4000m, [], 1500m),
        };
        var request = new SaveAgreementRequest(customer.Id, "Demo FY 2026-27", new DateOnly(2026, 4, 1), new DateOnly(2027, 3, 31), WeightBasis.Actual, null, null,
            detention ?? new DetentionPolicy(true, FreeTimeKind.Duration, 4, DetentionUnit.Hour, DetentionUnit.Hour, 200, PartUnitRounding.RoundUp, null, null),
            tax ?? new TaxSettings(TaxMode.Forward, 12, "996511", null), billing ?? BillingRules.Standard, [], rates);
        shape?.Invoke(request, rates);
        var agreement = await Ok<AgreementDto>(admin.PostJsonAsync("/api/v1/ops/agreements", request with { Rates = rates }));
        agreement = await Ok<AgreementDto>(admin.PostAsync($"/api/v1/ops/agreements/{agreement.Id}/activate", null));
        return new OpsScenario { Admin = admin, Anonymous = factory.CreateClient(), Customer = customer, Cfa = cfa, Pune = pune, Nashik = nashik, Agreement = agreement };
    }

    public static async Task<T> Ok<T>(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.ShouldBeTrue(await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    public Task<CreatedLrDto> CreateLrAsync(LocationDto? to = null, decimal kg = 1200, string reference = "PO-100", decimal? cbm = null, DateOnly? date = null) =>
        Ok<CreatedLrDto>(Admin.PostJsonAsync("/api/v1/ops/lrs", new CreateLrRequest(null, Customer.Id, reference, Cfa.Id, (to ?? Pune).Id, date ?? DateOnly.FromDateTime(DateTime.UtcNow), "Handle with care",
            [new LrLineRequest("SOAP", "Soap cartons", 100, "PKG", kg, cbm, "60x40x40", 100)])));

    public Task<TripDetailDto> CreateTripAsync(params Guid[] lrIds) =>
        Ok<TripDetailDto>(Admin.PostJsonAsync("/api/v1/ops/trips", new CreateTripRequest(Cfa.Id, null, "MH12AB1234", null, "Ramesh Patil", "9867012345", DateOnly.FromDateTime(DateTime.UtcNow), null, lrIds)));

    public Task<TripDetailDto> CfaEventAsync(Guid tripId, EventType type, DateTimeOffset at, string? key = null) =>
        Ok<TripDetailDto>(Admin.PostJsonAsync($"/api/v1/ops/trips/{tripId}/events", new CfaEventRequest(type, at, null, key)));

    /// <summary>Reached, loaded and departed, with the loading taking 40 minutes.</summary>
    public async Task<TripDetailDto> DepartAsync(Guid tripId, DateTimeOffset? from = null)
    {
        var t = from ?? Base;
        await CfaEventAsync(tripId, EventType.CfaReached, t);
        await CfaEventAsync(tripId, EventType.LoadingStarted, t.AddMinutes(10));
        await CfaEventAsync(tripId, EventType.LoadingCompleted, t.AddMinutes(50));
        return await CfaEventAsync(tripId, EventType.CfaDeparted, t.AddMinutes(60));
    }

    public static string Token(CreatedLrDto lr) => lr.QrPath["/q/".Length..];

    public static string Url(string token, string tail = "") => $"/api/v1/public/ops/lr/{token}{tail}";

    public Task<HttpResponseMessage> ArriveAsync(string token, double? lat, double? lon, double? accuracy, string? key = null, DateTimeOffset? capturedAt = null, string? error = null) =>
        Anonymous.PostJsonAsync(Url(token, "/arrival"), new ArrivalRequest(lat, lon, accuracy, capturedAt ?? (lat is null ? null : DateTimeOffset.UtcNow), error, key));

    /// <summary>At the Pune gate, with a good fix.</summary>
    public Task<HttpResponseMessage> ArriveAtPuneAsync(string token, string? key = null) => ArriveAsync(token, 18.7607, 73.8637, 12, key);

    public Task<HttpResponseMessage> UnloadAsync(string token, string? key = null) => Anonymous.PostJsonAsync(Url(token, "/unloading"), new UnloadingRequest(key));

    public static MultipartFormDataContent Files(params string[] seeds)
    {
        var form = new MultipartFormDataContent();
        foreach (var seed in seeds)
        {
            var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.Concat(Encoding.UTF8.GetBytes($"pod-{seed}")).ToArray();
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(part, "files", $"{seed}.png");
        }

        return form;
    }

    public Task<QrViewDto> ViewAsync(string token) => Ok<QrViewDto>(Anonymous.GetAsync(Url(token)));

    public Task<LrDetailDto> GetLrAsync(Guid id) => Ok<LrDetailDto>(Admin.GetAsync($"/api/v1/ops/lrs/{id}"));

    /// <summary>Takes an LR all the way to "unloading completed" with an arrival the office records at chosen times (so detention is predictable).</summary>
    public async Task<(CreatedLrDto Lr, TripDetailDto Trip)> DeliveredAsync(TimeSpan waited, LocationDto? to = null, decimal kg = 1200, DateTimeOffset? departed = null, DateOnly? date = null, decimal? cbm = null)
    {
        var lr = await CreateLrAsync(to, kg, date: date, cbm: cbm);
        var trip = await CreateTripAsync(lr.Lr.Lr.Id);
        await DepartAsync(trip.Trip.Id, departed);
        var arrive = (departed ?? Base).AddHours(4);
        (await Admin.PostJsonAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/destination-events", new DestinationEventRequest(EventType.DestinationArrival, arrive, "Driver had no signal"))).EnsureSuccessStatusCode();
        (await Admin.PostJsonAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/destination-events", new DestinationEventRequest(EventType.UnloadingCompleted, arrive + waited, "Recorded from the delivery note"))).EnsureSuccessStatusCode();
        return (lr, trip);
    }

    public async Task<PodDto> AcceptedPodAsync(Guid lrId)
    {
        (await Admin.PostAsync($"/api/v1/ops/lrs/{lrId}/pod", Files(Guid.NewGuid().ToString("N")))).EnsureSuccessStatusCode();
        return await Ok<PodDto>(Admin.PostJsonAsync($"/api/v1/ops/lrs/{lrId}/pod/accept", new PodDecisionRequest("Checked")));
    }

    public async Task<CalculationDto> CalculateAsync(Guid lrId)
    {
        var result = await Ok<CalculateResultDto>(Admin.PostJsonAsync("/api/v1/ops/billing/calculate", new CalculateRequest([lrId])));
        result.Failures.ShouldBeEmpty();
        return result.Calculations.Single();
    }

    public async Task<CalculationDto> ReviewedAsync(Guid lrId)
    {
        var calc = await CalculateAsync(lrId);
        return await Ok<CalculationDto>(Admin.PostJsonAsync($"/api/v1/ops/billing/calculations/{calc.Id}/review", new ReviewCalculationRequest("ok")));
    }

    public void Dispose()
    {
        Admin.Dispose();
        Anonymous.Dispose();
    }
}

[Collection(ApiCollection.Name)]
public class TransportOpsRequestAndDispatchApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task An_lr_keeps_indent_net_weight_and_cubic_feet_takes_units_from_the_master_and_holds_at_most_ten_invoice_copies()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        CreateLrRequest Body(string unit) => new(null, s.Customer.Id, "PO-7", s.Cfa.Id, s.Pune.Id, null, null, [new LrLineRequest(null, null, 5, unit, 100, null, null, 5, 100, 90)], "IND-42");

        (await s.Admin.PostJsonAsync("/api/v1/ops/lrs", Body("ZZZ"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // not in the master
        (await s.Admin.PostJsonAsync("/api/v1/ops/lrs", new CreateLrRequest(null, s.Customer.Id, null, s.Cfa.Id, s.Pune.Id, null, null,
            [new LrLineRequest(null, null, 5, "BOX", 100, null, null, 5, null, 120)]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // net more than gross

        var lr = (await OpsScenario.Ok<CreatedLrDto>(s.Admin.PostJsonAsync("/api/v1/ops/lrs", Body("BOX")))).Lr;
        lr.IndentNo.ShouldBe("IND-42");
        lr.Lines.Single().NetWeightKg.ShouldBe(90);
        lr.Lines.Single().VolumeCuFt.ShouldBe(100);
        lr.Lines.Single().VolumeCbm.ShouldBe(2.832m); // 100 cu ft, kept in cubic metres for rating

        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52 };
        for (var i = 0; i < 10; i++)
        {
            using var form = new MultipartFormDataContent { { new ByteArrayContent(png), "files", $"invoice{i}.png" } };
            (await s.Admin.PostAsync($"/api/v1/ops/lrs/{lr.Lr.Id}/files", form)).IsSuccessStatusCode.ShouldBeTrue();
        }

        using var eleventh = new MultipartFormDataContent { { new ByteArrayContent(png), "files", "invoice10.png" } };
        (await s.Admin.PostAsync($"/api/v1/ops/lrs/{lr.Lr.Id}/files", eleventh)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await OpsScenario.Ok<LrDetailDto>(s.Admin.GetAsync($"/api/v1/ops/lrs/{lr.Lr.Id}"))).Files!.Count.ShouldBe(10);
    }

    [Fact]
    public async Task An_incomplete_request_cannot_reach_dispatch_until_it_is_complete_and_then_becomes_one_lr_only()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var created = await OpsScenario.Ok<RequestDto>(s.Admin.PostJsonAsync("/api/v1/ops/requests", new SaveRequestRequest(s.Customer.Id, null, null, "RE: Dispatch 500 cartons", s.Cfa.Id, null, null, "Normal", null, null, null, [])));
        created.Status.ShouldBe(RequestStatus.Received);
        created.Number.ShouldStartWith("REQ-");

        var checkedOnce = await OpsScenario.Ok<RequestDto>(s.Admin.PostAsync($"/api/v1/ops/requests/{created.Id}/validate", null));
        checkedOnce.Status.ShouldBe(RequestStatus.Incomplete);
        checkedOnce.Issues.ShouldContain(i => i.Contains("Destination"));
        checkedOnce.Issues.ShouldContain(i => i.Contains("PO")); // this customer insists on the PO reference
        (await s.Admin.PostAsync($"/api/v1/ops/requests/{created.Id}/ready", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Admin.PostJsonAsync("/api/v1/ops/lrs", new CreateLrRequest(created.Id, null, null, null, null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        // A stale copy is refused: the check above changed the request.
        (await s.Admin.PutJsonAsync($"/api/v1/ops/requests/{created.Id}", new SaveRequestRequest(s.Customer.Id, "PO-0", null, null, s.Cfa.Id, s.Pune.Id, null, null, null, null, null, [], created.Version))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var current = await OpsScenario.Ok<RequestDto>(s.Admin.GetAsync($"/api/v1/ops/requests/{created.Id}"));
        var fixedUp = await OpsScenario.Ok<RequestDto>(s.Admin.PutJsonAsync($"/api/v1/ops/requests/{created.Id}", new SaveRequestRequest(s.Customer.Id, "PO-7788", null, "RE: Dispatch 500 cartons", s.Cfa.Id, s.Pune.Id,
            DateOnly.FromDateTime(DateTime.UtcNow), "High", "22 ft", "Gate 3", null, [new MaterialLineDto("SOAP", "Soap cartons", 500, "PKG", 2500, 20, "60x40x40", 500)], current.Version)));
        fixedUp.Status.ShouldBe(RequestStatus.UnderReview); // a change after checking means checking again

        (await OpsScenario.Ok<RequestDto>(s.Admin.PostAsync($"/api/v1/ops/requests/{created.Id}/validate", null))).Status.ShouldBe(RequestStatus.Validated);
        (await OpsScenario.Ok<RequestDto>(s.Admin.PostAsync($"/api/v1/ops/requests/{created.Id}/ready", null))).Status.ShouldBe(RequestStatus.ReadyForDispatch);

        var lr = await OpsScenario.Ok<CreatedLrDto>(s.Admin.PostJsonAsync("/api/v1/ops/lrs", new CreateLrRequest(created.Id, null, null, null, null, null, null, null)));
        lr.Lr.Lr.Number.ShouldStartWith("LR-");
        lr.Lr.Lr.TotalWeightKg.ShouldBe(2500m);
        lr.Lr.Lr.CustomerReference.ShouldBe("PO-7788");
        lr.Lr.RequestId.ShouldBe(created.Id);
        (await OpsScenario.Ok<RequestDto>(s.Admin.GetAsync($"/api/v1/ops/requests/{created.Id}"))).Status.ShouldBe(RequestStatus.Converted);
        (await s.Admin.PostJsonAsync("/api/v1/ops/lrs", new CreateLrRequest(created.Id, null, null, null, null, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task The_original_email_attachment_is_kept_against_the_request_and_only_real_documents_are_accepted()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var request = await OpsScenario.Ok<RequestDto>(s.Admin.PostJsonAsync("/api/v1/ops/requests", new SaveRequestRequest(s.Customer.Id, "PO-1", null, "mail", s.Cfa.Id, s.Pune.Id, null, null, null, null, null, [])));

        var ok = await s.Admin.PostAsync($"/api/v1/ops/requests/{request.Id}/files", OpsScenario.Files("email"));
        ok.StatusCode.ShouldBe(HttpStatusCode.OK);
        var disguised = new MultipartFormDataContent { { new ByteArrayContent(Encoding.UTF8.GetBytes("MZ not a pdf")), "files", "invoice.pdf" } };
        (await s.Admin.PostAsync($"/api/v1/ops/requests/{request.Id}/files", disguised)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var reread = await OpsScenario.Ok<RequestDto>(s.Admin.GetAsync($"/api/v1/ops/requests/{request.Id}"));
        reread.Files.Count.ShouldBe(1);
        (await s.Admin.GetAsync($"/api/v1/ops/request-files/{reread.Files[0].Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_trip_carries_several_distributor_stops_each_with_its_own_lr_and_the_cfa_timeline_is_measured()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var toPune = await s.CreateLrAsync(s.Pune);
        var toNashik = await s.CreateLrAsync(s.Nashik);
        var anotherPune = await s.CreateLrAsync(s.Pune, reference: "PO-2");

        var trip = await s.CreateTripAsync(toPune.Lr.Lr.Id, toNashik.Lr.Lr.Id, anotherPune.Lr.Lr.Id);

        trip.Stops.Count.ShouldBe(2); // Pune and Nashik: one stop per destination
        trip.Stops.Single(x => x.DestinationLocationId == s.Pune.Id).Lrs.Count.ShouldBe(2);
        trip.Stops.Select(x => x.Sequence).ShouldBe([1, 2]);
        (await s.GetLrAsync(toPune.Lr.Lr.Id)).Lr.Status.ShouldBe(DeliveryStatus.Assigned);

        // Events are once-only and in order.
        (await s.Admin.PostJsonAsync($"/api/v1/ops/trips/{trip.Trip.Id}/events", new CfaEventRequest(EventType.LoadingStarted, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var departed = await s.DepartAsync(trip.Trip.Id);
        departed.Trip.Status.ShouldBe(TripStatus.InTransit);
        departed.Trip.LoadingMinutes.ShouldBe(40);
        departed.Trip.CfaTurnaroundMinutes.ShouldBe(60);
        (await s.Admin.PostJsonAsync($"/api/v1/ops/trips/{trip.Trip.Id}/events", new CfaEventRequest(EventType.CfaDeparted, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        foreach (var lr in new[] { toPune, toNashik, anotherPune })
        {
            var detail = await s.GetLrAsync(lr.Lr.Lr.Id);
            detail.Lr.Status.ShouldBe(DeliveryStatus.InTransit);
            detail.Lr.DepartedAt.ShouldNotBeNull();
            detail.Events.Select(e => e.Type).ShouldBe([EventType.CfaReached, EventType.LoadingStarted, EventType.LoadingCompleted, EventType.CfaDeparted]);
        }
    }

    [Fact]
    public async Task A_cfa_time_can_be_corrected_with_a_reason_and_the_history_shows_both()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var lr = await s.CreateLrAsync();
        var trip = await s.CreateTripAsync(lr.Lr.Lr.Id);
        await s.DepartAsync(trip.Trip.Id);

        var noReason = await s.Admin.PostJsonAsync($"/api/v1/ops/trips/{trip.Trip.Id}/events/correct", new CorrectEventRequest(EventType.LoadingStarted, OpsScenario.Base.AddMinutes(20), " "));
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var outOfOrder = await s.Admin.PostJsonAsync($"/api/v1/ops/trips/{trip.Trip.Id}/events/correct", new CorrectEventRequest(EventType.LoadingStarted, OpsScenario.Base.AddMinutes(55), "typo"));
        outOfOrder.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var corrected = await OpsScenario.Ok<TripDetailDto>(s.Admin.PostJsonAsync($"/api/v1/ops/trips/{trip.Trip.Id}/events/correct", new CorrectEventRequest(EventType.LoadingStarted, OpsScenario.Base.AddMinutes(20), "Gate log shows 20 minutes")));

        corrected.Trip.LoadingMinutes.ShouldBe(30);
        corrected.Events.ShouldContain(e => e.Type == EventType.Correction && e.Remarks!.Contains("Gate log") && e.CorrectsEventId != null);
        corrected.Events.Count(e => e.Type == EventType.LoadingStarted).ShouldBe(1); // the original stays
    }

    [Fact]
    public async Task A_vehicle_with_a_malformed_registration_is_refused_and_an_lr_already_on_a_trip_cannot_join_another()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var lr = await s.CreateLrAsync();

        (await s.Admin.PostJsonAsync("/api/v1/ops/trips", new CreateTripRequest(s.Cfa.Id, null, "NOT A PLATE", null, null, null, null, null, [lr.Lr.Lr.Id]))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await s.CreateTripAsync(lr.Lr.Lr.Id);
        (await s.Admin.PostJsonAsync("/api/v1/ops/trips", new CreateTripRequest(s.Cfa.Id, null, "MH12AB1234", null, null, null, null, null, [lr.Lr.Lr.Id]))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Each_lr_gets_its_own_number_and_the_printout_carries_the_qr_path_but_never_the_secret_in_the_list()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var a = await s.CreateLrAsync();
        var b = await s.CreateLrAsync();

        a.Lr.Lr.Number.ShouldNotBe(b.Lr.Lr.Number);
        Regex.IsMatch(a.QrPath, "^/q/[A-Za-z0-9_-]{43}$").ShouldBeTrue();
        var print = await OpsScenario.Ok<LrPrintDto>(s.Admin.GetAsync($"/api/v1/ops/lrs/{a.Lr.Lr.Id}/print"));
        print.QrPath.ShouldBe(a.QrPath); // a reprint shows the same code the driver already holds
        print.PrintCount.ShouldBe(1);
        var list = await s.Admin.GetStringAsync($"/api/v1/ops/lrs?search={a.Lr.Lr.Number}");
        list.ShouldNotContain(OpsScenario.Token(a));
        (await s.GetLrAsync(a.Lr.Lr.Id)).PrintCount.ShouldBe(1);
    }

    [Fact]
    public async Task Customers_and_locations_validate_gstin_coordinates_and_uniqueness()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var badGstin = await s.Admin.PostJsonAsync("/api/v1/ops/customers", new SaveCustomerRequest("BADG", "Bad", "27AAAAA0000A1Z5", null, null, null, null, null, null, null, null, null, null, null, 30, null, null, null, null, ActiveStatus.Active));
        badGstin.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var swapped = await s.Admin.PostJsonAsync("/api/v1/ops/locations", new SaveLocationRequest("SWAPPED", "Swapped", LocationKind.Distributor, null, "Line", "Pune", "Maharashtra", null, 73.8, 18.7, 150, null, ActiveStatus.Active));
        swapped.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PostJsonAsync("/api/v1/ops/locations", new SaveLocationRequest(s.Pune.Code, "Again", LocationKind.Distributor, null, "Line", "Pune", "Maharashtra", null, 18.7, 73.8, 150, null, ActiveStatus.Active))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Admin.PostJsonAsync("/api/v1/ops/locations", new SaveLocationRequest("TINY", "Tiny", LocationKind.Distributor, null, "Line", "Pune", "Maharashtra", null, 18.7, 73.8, 5, null, ActiveStatus.Active))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Without_the_permissions_the_office_pages_answer_403_and_without_signing_in_401()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        using var nobody = await factory.UserWithPermissionsAsync(s.Admin, "contracts.read");
        using var reader = await factory.UserWithPermissionsAsync(s.Admin, OpsPermissions.Read);

        (await nobody.GetAsync("/api/v1/ops/lrs")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await reader.GetAsync("/api/v1/ops/lrs")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await reader.PostJsonAsync("/api/v1/ops/customers", new SaveCustomerRequest("X", "X", null, null, null, null, null, null, null, null, null, null, null, null, 30, null, null, null, null, ActiveStatus.Active))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await s.Anonymous.GetAsync("/api/v1/ops/lrs")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Another_organisations_records_are_not_visible()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        using var other = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);

        (await other.GetAsync($"/api/v1/ops/customers/{s.Customer.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var theirs = await OpsScenario.Ok<PagedResult<CustomerDto>>(other.GetAsync("/api/v1/ops/customers"));
        theirs.Items.ShouldNotContain(c => c.Id == s.Customer.Id);
    }
}
