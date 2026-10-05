using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Domain;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.DeliveryScenario;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DeliveryExecutionApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task A_delivery_is_created_from_an_external_reference_assigned_and_visible_only_to_its_transporter()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync();

        delivery.Summary.Status.ShouldBe(DeliveryStatus.Assigned);
        delivery.Summary.Number.ShouldStartWith("DLV-");
        delivery.Summary.PodStatus.ShouldBe(PodStatus.Pending);
        delivery.Items.Single().DispatchedQuantity.ShouldBe(100);

        (await s.Vendor.GetAsync($"/api/v1/deliveries/{delivery.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await s.Rival.GetAsync($"/api/v1/deliveries/{delivery.Summary.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound); // 404, not 403
        var theirs = await (await s.Rival.GetAsync("/api/v1/deliveries?pageSize=100")).ReadAsync<PagedResult<DeliverySummaryDto>>();
        theirs.Items.ShouldNotContain(d => d.Id == delivery.Summary.Id);

        var mine = await (await s.Vendor.GetAsync($"/api/v1/deliveries?search={s.ShipmentReference}")).ReadAsync<PagedResult<DeliverySummaryDto>>();
        mine.Items.Single().Id.ShouldBe(delivery.Summary.Id);
    }

    [Fact]
    public async Task A_vendor_cannot_create_or_cancel_deliveries_and_validation_is_enforced_on_the_server()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        (await s.Vendor.PostJsonAsync("/api/v1/deliveries", s.Request())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var delivery = await s.DeliveryAsync();
        (await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{delivery.Summary.Id}/cancel", new ReasonRequest("no"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var bad = await s.Admin.PostJsonAsync("/api/v1/deliveries", s.Request() with { CustomerName = "", Items = [] });
        bad.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_driver_takes_a_delivery_through_start_arrive_and_complete_with_the_location_recorded()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();

        arrived.Summary.Status.ShouldBe(DeliveryStatus.Arrived);
        arrived.ActualArrivalAt.ShouldNotBeNull();
        arrived.Events.Select(e => e.Type).ShouldBe([DeliveryEventType.Created, DeliveryEventType.Assigned, DeliveryEventType.Started, DeliveryEventType.Arrived]);
        arrived.Events[^1].Latitude.ShouldBe(18.5204);
        arrived.Events[^1].DeviceReference.ShouldBe("device-1");

        var completed = await s.CompleteAsync(arrived, Complete(DeliveryOutcome.Full, null, null, Qty(arrived.Items[0], 100)));
        completed.StatusCode.ShouldBe(HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        var done = await completed.ReadAsync<DeliveryDto>();
        done.Summary.Status.ShouldBe(DeliveryStatus.Delivered);
        done.Summary.PodStatus.ShouldBe(PodStatus.Draft); // delivered is not the same as proved
        done.Reconciliation.Single().Reconciled.ShouldBeTrue();
    }

    [Fact]
    public async Task Skipping_a_step_is_refused()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var delivery = await s.DeliveryAsync();

        var early = await s.CompleteAsync(delivery, Complete(DeliveryOutcome.Full, null, null, Qty(delivery.Items[0], 100)));
        early.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await early.ProblemCodeAsync()).ShouldBe("deliveries.invalid_state");
        (await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{delivery.Summary.Id}/arrive", Here())).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Quantities_are_checked_on_the_server_and_a_mismatch_is_reported_exactly()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();
        var item = arrived.Items[0];

        (await s.CompleteAsync(arrived, Complete(DeliveryOutcome.Full, null, null, Qty(item, 120)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // over-delivery
        (await s.CompleteAsync(arrived, Complete(DeliveryOutcome.Shortage, null, null, Qty(item, -5, @short: 105)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // negative
        var noReason = Complete(DeliveryOutcome.Shortage, null, null, Qty(item, 97, @short: 3) with { ShortageReasonCode = null });
        (await s.CompleteAsync(arrived, noReason)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var unknownReason = Complete(DeliveryOutcome.Shortage, null, null, Qty(item, 97, @short: 3) with { ShortageReasonCode = "BECAUSE" });
        (await s.CompleteAsync(arrived, unknownReason)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var mismatch = await s.CompleteAsync(arrived, Complete(DeliveryOutcome.Shortage, null, null, Qty(item, 90, @short: 3)));
        mismatch.StatusCode.ShouldBe(HttpStatusCode.OK, await mismatch.Content.ReadAsStringAsync());
        var done = await mismatch.ReadAsync<DeliveryDto>();
        done.HasQuantityMismatch.ShouldBeTrue();
        done.Reconciliation.Single().Unaccounted.ShouldBe(7);
        done.Items[0].Unaccounted.ShouldBe(7);
        var exceptions = await (await s.Admin.GetAsync($"/api/v1/delivery-exceptions?deliveryId={done.Summary.Id}")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        exceptions.Items.Select(e => e.Type).ShouldBe([ExceptionType.QuantityMismatch, ExceptionType.Shortage], ignoreOrder: true);
    }

    [Fact]
    public async Task A_failed_attempt_leaves_the_delivery_open_and_a_failed_delivery_raises_an_exception_and_can_be_rescheduled()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();
        var id = arrived.Summary.Id;

        (await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{id}/attempt", new AttemptRequest("NOT_A_REASON", null, null, null, Here()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var attempt = await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{id}/attempt", new AttemptRequest("CUSTOMER_UNAVAILABLE", "Shop shut", "Back after lunch", null, Here()));
        (await attempt.ReadAsync<DeliveryDto>()).Summary.Status.ShouldBe(DeliveryStatus.Attempted);

        var failed = await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{id}/fail", new FailDeliveryRequest("SITE_CLOSED", "Closed for the day", Here()));
        var after = await failed.ReadAsync<DeliveryDto>();
        after.Summary.Status.ShouldBe(DeliveryStatus.Failed);
        after.Attempts.Select(a => a.AttemptNumber).ShouldBe([1, 2]);
        after.Summary.OpenExceptions.ShouldBe(1);

        var again = await s.Admin.PostJsonAsync($"/api/v1/deliveries/{id}/reschedule", new RescheduleRequest(DateTimeOffset.UtcNow.AddDays(1), null, null));
        (await again.ReadAsync<DeliveryDto>()).Summary.Status.ShouldBe(DeliveryStatus.Assigned);
        (await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{id}/reschedule", new RescheduleRequest(DateTimeOffset.UtcNow.AddDays(1), null, null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_customer_refusal_records_every_item_as_rejected_and_raises_a_refusal_exception()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();

        (await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{arrived.Summary.Id}/refuse", new RefuseDeliveryRequest("", null, null, false, Here()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var refused = await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{arrived.Summary.Id}/refuse", new RefuseDeliveryRequest("WRONG_ITEM", "Anil", "Not what we ordered", true, Here()));
        var after = await refused.ReadAsync<DeliveryDto>();

        after.Summary.Status.ShouldBe(DeliveryStatus.Refused);
        after.Items[0].RejectedQuantity.ShouldBe(100);
        after.Discrepancies.Single().CustomerAcknowledged.ShouldBeTrue();
        var exceptions = await (await s.Admin.GetAsync($"/api/v1/delivery-exceptions?deliveryId={after.Summary.Id}")).ReadAsync<PagedResult<ExceptionSummaryDto>>();
        exceptions.Items.Single().Type.ShouldBe(ExceptionType.CustomerRefusal);
    }

    [Fact]
    public async Task A_partial_delivery_must_say_what_becomes_of_the_rest_and_raises_a_partial_delivery_exception()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();
        var item = arrived.Items[0];

        (await s.CompleteAsync(arrived, Complete(DeliveryOutcome.Partial, null, null, Qty(item, 80, rejected: 20)))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var ok = await s.CompleteAsync(arrived, Complete(DeliveryOutcome.Partial, null, RemainingDisposition.Backorder, Qty(item, 80, rejected: 20)));
        var done = await ok.ReadAsync<DeliveryDto>();

        done.Summary.Status.ShouldBe(DeliveryStatus.PartiallyDelivered);
        done.RemainingDisposition.ShouldBe(RemainingDisposition.Backorder);
        done.Summary.HasDiscrepancy.ShouldBeTrue();
    }

    [Fact]
    public async Task The_one_time_code_is_emailed_on_arrival_verified_on_the_server_and_never_returned()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var original = DeliverySettingDefaults.For(DeliverySettingKeys.PodRules)!;
        await s.SetSettingAsync(DeliverySettingKeys.PodRules, new PodRulesSetting(false, true, true, true, 1, false, true, false, 100, 30, 3));
        try
        {
            var email = $"otp-{Guid.NewGuid():N}@example.test";
            var arrived = await s.ArrivedAsync(s.Request(email: email));
            arrived.OtpIssued.ShouldBeTrue();
            arrived.OtpVerified.ShouldBeFalse();

            var mail = factory.Services.GetRequiredService<CapturingEmailSender>();
            var message = mail.Sent.Single(m => m.To == email);
            var code = System.Text.RegularExpressions.Regex.Match(message.TextBody, @"\b\d{6}\b").Value;
            code.Length.ShouldBe(6);
            (await (await s.Vendor.GetAsync($"/api/v1/deliveries/{arrived.Summary.Id}")).Content.ReadAsStringAsync()).ShouldNotContain(code);

            var wrong = await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{arrived.Summary.Id}/otp/verify", new VerifyOtpRequest(code == "000000" ? "111111" : "000000", Here()));
            wrong.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await wrong.ProblemCodeAsync()).ShouldBe("deliveries.otp_wrong");

            var right = await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{arrived.Summary.Id}/otp/verify", new VerifyOtpRequest(code, Here()));
            right.StatusCode.ShouldBe(HttpStatusCode.OK, await right.Content.ReadAsStringAsync());
            (await right.ReadAsync<DeliveryDto>()).OtpVerified.ShouldBeTrue();

            var pod = await s.CompletedAsync(arrived, proof: new ProofRequest(ProofMethod.Otp, "Anil Kumar", null, null, null, false, false));
            pod.OtpVerified.ShouldBeTrue();
            pod.Method.ShouldBe(ProofMethod.Otp);
        }
        finally
        {
            await s.SetSettingAsync(DeliverySettingKeys.PodRules, original);
        }
    }

    [Fact]
    public async Task Too_many_wrong_codes_lock_the_delivery_until_a_new_code_is_issued()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var original = DeliverySettingDefaults.For(DeliverySettingKeys.PodRules)!;
        await s.SetSettingAsync(DeliverySettingKeys.PodRules, new PodRulesSetting(false, true, true, true, 1, false, true, false, 100, 30, 2));
        try
        {
            var arrived = await s.ArrivedAsync(s.Request(email: $"otp-{Guid.NewGuid():N}@example.test"));
            var url = $"/api/v1/deliveries/{arrived.Summary.Id}/otp/verify";
            (await s.Vendor.PostJsonAsync(url, new VerifyOtpRequest("123450", Here()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            (await s.Vendor.PostJsonAsync(url, new VerifyOtpRequest("123451", Here()))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            var locked = await s.Vendor.PostJsonAsync(url, new VerifyOtpRequest("123452", Here()));
            locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await locked.ProblemCodeAsync()).ShouldBe("deliveries.otp_locked");
        }
        finally
        {
            await s.SetSettingAsync(DeliverySettingKeys.PodRules, original);
        }
    }

    [Fact]
    public async Task A_shipment_that_is_dispatched_opens_a_delivery_for_each_drop_once()
    {
        using var scenario = await ShipmentScenario.CreateAsync(factory);
        var orders = new[] { await scenario.OrderAsync(3_000m, "Surat"), await scenario.OrderAsync(4_000m, "Vadodara") };
        var tendered = await scenario.TenderedAsync(orders);
        (await scenario.Vendor.PostJsonAsync($"/api/v1/shipments/{tendered.Summary.Id}/accept", new Tms.Modules.Shipments.Application.AcceptRequest(scenario.Vehicle.Id, scenario.Driver.Id))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await scenario.Admin.PostAsync($"/api/v1/shipments/{tendered.Summary.Id}/dispatch", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        PagedResult<DeliverySummaryDto>? found = null;
        for (var i = 0; i < 50 && found is not { TotalCount: 2 }; i++)
        {
            await Task.Delay(100);
            found = await (await scenario.Admin.GetAsync($"/api/v1/deliveries?search={tendered.Summary.Number}")).ReadAsync<PagedResult<DeliverySummaryDto>>();
        }

        found!.Items.Count.ShouldBe(2);
        found.Items.ShouldAllBe(d => d.Status == DeliveryStatus.Assigned && d.ShipmentReference == tendered.Summary.Number && d.TransporterId == scenario.Transporter.Id);
        found.Items.Select(d => d.CustomerName).Distinct().Count().ShouldBeGreaterThan(0);

        var raw = await (await scenario.Admin.GetAsync($"/api/v1/deliveries/{found.Items[0].Id}")).Content.ReadAsStringAsync();
        var detail = System.Text.Json.JsonSerializer.Deserialize<DeliveryDto>(raw, ApiExtensions.Json)!;
        detail.LrNumber.ShouldNotBeNull(); // the lorry receipt issued at dispatch
        detail.Summary.VehicleReference.ShouldBe(scenario.Vehicle.RegistrationNumber);
        detail.Items.Single().DispatchedQuantity.ShouldBeGreaterThan(0);
    }
}
