using Tms.Modules.TransportOps.Domain;

namespace Tms.UnitTests.TransportOps;

public class TripTests
{
    private static Trip NewTrip() => Trip.Create(Guid.NewGuid(), "TRP-1", Guid.NewGuid(), null, "MH12AB1234", null, "Driver", null, null, null);

    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cfa_events_must_happen_once_and_in_order()
    {
        var trip = NewTrip();

        trip.RecordCfaEvent(EventType.LoadingStarted, T0).Error.Code.ShouldBe("ops.event_order");
        trip.RecordCfaEvent(EventType.CfaReached, T0).IsSuccess.ShouldBeTrue();
        trip.RecordCfaEvent(EventType.CfaReached, T0.AddMinutes(1)).Error.Code.ShouldBe("ops.event_duplicate");
        trip.RecordCfaEvent(EventType.LoadingStarted, T0.AddMinutes(-5)).Error.Code.ShouldBe("ops.event_time_order");
        trip.RecordCfaEvent(EventType.LoadingStarted, T0.AddMinutes(10)).IsSuccess.ShouldBeTrue();
        trip.RecordCfaEvent(EventType.LoadingCompleted, T0.AddMinutes(70)).IsSuccess.ShouldBeTrue();
        trip.RecordCfaEvent(EventType.CfaDeparted, T0.AddMinutes(80)).IsSuccess.ShouldBeTrue();

        trip.Status.ShouldBe(TripStatus.InTransit);
        trip.HasLeft.ShouldBeTrue();
    }

    [Fact]
    public void A_correction_must_stay_between_the_events_either_side_of_it()
    {
        var trip = NewTrip();
        trip.RecordCfaEvent(EventType.CfaReached, T0);
        trip.RecordCfaEvent(EventType.LoadingStarted, T0.AddMinutes(10));
        trip.RecordCfaEvent(EventType.LoadingCompleted, T0.AddMinutes(60));

        trip.CorrectCfaTime(EventType.LoadingStarted, T0.AddMinutes(20)).IsSuccess.ShouldBeTrue();
        trip.CorrectCfaTime(EventType.LoadingStarted, T0.AddMinutes(90)).Error.Code.ShouldBe("ops.event_time_order");
        trip.CorrectCfaTime(EventType.CfaDeparted, T0).Error.Code.ShouldBe("ops.event_missing");
    }

    [Fact]
    public void A_trip_that_has_left_cannot_change_vehicle_or_be_cancelled()
    {
        var trip = NewTrip();
        foreach (var (type, at) in new[] { (EventType.CfaReached, T0), (EventType.LoadingStarted, T0.AddMinutes(1)), (EventType.LoadingCompleted, T0.AddMinutes(2)), (EventType.CfaDeparted, T0.AddMinutes(3)) })
        {
            trip.RecordCfaEvent(type, at);
        }

        trip.Update("MH12ZZ9999", null, null, null, null, null, null).Error.Code.ShouldBe("ops.trip_left");
        trip.Cancel().Error.Code.ShouldBe("ops.trip_left");
    }

    [Fact]
    public void A_trip_with_several_destinations_completes_only_when_every_stop_is_unloaded()
    {
        var trip = NewTrip();
        var a = trip.AddStop(Guid.NewGuid());
        var b = trip.AddStop(Guid.NewGuid());
        a.Unloaded(T0);
        trip.CompleteIfAllUnloaded();
        trip.Status.ShouldNotBe(TripStatus.Completed);

        b.Unloaded(T0.AddHours(1));
        trip.CompleteIfAllUnloaded();
        trip.Status.ShouldBe(TripStatus.Completed);
        (a.Sequence, b.Sequence).ShouldBe((1, 2));
    }
}

public class LrAndPodTests
{
    private static Lr NewLr() => Lr.Create(Guid.NewGuid(), "LR-000001", Guid.NewGuid(), null, "PO-1", Guid.NewGuid(), Guid.NewGuid(), null, null, [("S1", "Soap", 10, "PKG", 500, null, null, 10, null, null, null)]);

    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Arrival_needs_the_vehicle_to_be_in_transit_and_unloading_needs_arrival()
    {
        var lr = NewLr();

        lr.Arrive(T0).Error.Code.ShouldBe("ops.arrival_state");
        lr.CompleteUnloading(T0).Error.Code.ShouldBe("ops.unloading_state");

        lr.ApplyCfaEvent(EventType.CfaDeparted, T0);
        lr.Arrive(T0.AddHours(3)).IsSuccess.ShouldBeTrue();
        lr.Arrive(T0.AddHours(4)).IsFailure.ShouldBeTrue(); // not a second time
        lr.CompleteUnloading(T0.AddHours(5)).IsSuccess.ShouldBeTrue();
        lr.CompleteUnloading(T0.AddHours(6)).IsFailure.ShouldBeTrue();
        lr.Status.ShouldBe(DeliveryStatus.UnloadingCompleted);
    }

    [Fact]
    public void An_lr_totals_its_lines_and_never_exposes_the_secret_only_its_hash_and_a_short_reference()
    {
        var lr = NewLr();
        lr.IssueToken(new string('a', 64), "cipher", T0, T0.AddDays(14));

        (lr.TotalWeightKg, lr.TotalPackages).ShouldBe((500m, 10));
        lr.TokenRef.ShouldBe(new string('a', 12));
        lr.TokenActiveAt(T0.AddDays(13)).ShouldBeTrue();
        lr.TokenActiveAt(T0.AddDays(15)).ShouldBeFalse();
        lr.RevokeToken(T0.AddDays(1));
        lr.TokenActiveAt(T0.AddDays(2)).ShouldBeFalse();
    }

    [Fact]
    public void A_pod_is_received_but_never_accepted_by_uploading()
    {
        var pod = Pod.Create(Guid.NewGuid(), Guid.NewGuid());
        pod.Status.ShouldBe(PodStatus.NotYetDue);

        pod.MarkPending(T0).IsSuccess.ShouldBeTrue();
        pod.Accept(null, T0, null).Error.Code.ShouldBe("ops.pod_state"); // nothing to accept yet
        pod.AddDocuments([Document(pod, 1, "a")], T0).IsSuccess.ShouldBeTrue();

        pod.Status.ShouldBe(PodStatus.Received);
    }

    [Fact]
    public void A_rejected_pod_keeps_its_files_when_a_corrected_one_arrives_as_a_new_submission()
    {
        var pod = Pod.Create(Guid.NewGuid(), Guid.NewGuid());
        var first = pod.BeginSubmission();
        pod.AddDocuments([Document(pod, first, "one")], T0);
        pod.Reject(null, T0, "Unsigned").IsSuccess.ShouldBeTrue();
        pod.Reject(null, T0, "again").IsFailure.ShouldBeTrue();

        var second = pod.BeginSubmission();
        pod.AddDocuments([Document(pod, second, "two")], T0.AddHours(1));

        (first, second).ShouldBe((1, 2));
        pod.Documents.Count.ShouldBe(2);
        pod.Status.ShouldBe(PodStatus.Received);
        pod.RejectionReason.ShouldBeNull();
    }

    [Fact]
    public void A_rejection_needs_a_reason_and_an_accepted_pod_cannot_be_changed()
    {
        var pod = Pod.Create(Guid.NewGuid(), Guid.NewGuid());
        pod.AddDocuments([Document(pod, pod.BeginSubmission(), "x")], T0);

        pod.Reject(null, T0, " ").Error.Code.ShouldBe("ops.pod_reason");
        pod.Accept(null, T0, null).IsSuccess.ShouldBeTrue();
        pod.AddDocuments([Document(pod, 2, "y")], T0).Error.Code.ShouldBe("ops.pod_accepted");
        pod.Reviews.Count.ShouldBe(1);
    }

    private static PodDocument Document(Pod pod, int submission, string hash) =>
        PodDocument.Create(pod.TenantId, pod.Id, submission, $"k/{hash}", $"{hash}.png", "image/png", 10, hash, PodSource.QrWeb, null, T0, null, null, null);
}

public class DeliveryRequestTests
{
    private static DeliveryRequest Request() => DeliveryRequest.Create(Guid.NewGuid(), "REQ-1", Guid.NewGuid(), DateTimeOffset.UtcNow);

    [Fact]
    public void An_incomplete_request_lists_what_is_missing_and_cannot_become_ready()
    {
        var r = Request();

        var issues = r.Validate([]).Value;

        issues.Count.ShouldBeGreaterThanOrEqualTo(4);
        r.Status.ShouldBe(RequestStatus.Incomplete);
        r.MarkReady().Error.Code.ShouldBe("ops.request_not_validated");
    }

    [Fact]
    public void A_complete_request_is_validated_then_ready_then_converted_once()
    {
        var r = Request();
        r.Edit(r.CustomerId, "PO-1", DateTimeOffset.UtcNow, null, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 6), "Normal", null, null, null);
        r.SetLines([RequestLine.Create(r.TenantId, r.Id, "S1", "Soap", 10, "PKG", 100, null, null, 10, null)]);

        r.Validate([]).Value.ShouldBeEmpty();
        r.Status.ShouldBe(RequestStatus.Validated);
        r.MarkReady().IsSuccess.ShouldBeTrue();
        r.Convert(Guid.NewGuid()).IsSuccess.ShouldBeTrue();
        r.Convert(Guid.NewGuid()).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Editing_a_validated_request_sends_it_back_for_checking()
    {
        var r = Request();
        r.Edit(r.CustomerId, "PO-1", DateTimeOffset.UtcNow, null, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 6), "Normal", null, null, null);
        r.SetLines([RequestLine.Create(r.TenantId, r.Id, null, "Soap", 10, "PKG", 100, null, null, 10, null)]);
        r.Validate([]);

        r.Edit(r.CustomerId, "PO-2", DateTimeOffset.UtcNow, null, r.CfaLocationId, r.DestinationLocationId, r.RequestedDate, "High", null, null, null);

        r.Status.ShouldBe(RequestStatus.UnderReview);
    }

    [Fact]
    public void A_customer_can_insist_on_extra_details()
    {
        var r = Request();
        r.Edit(r.CustomerId, null, DateTimeOffset.UtcNow, null, Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 10, 6), "Normal", null, null, null);
        r.SetLines([RequestLine.Create(r.TenantId, r.Id, null, "Soap", 10, "PKG", 100, null, null, 10, null)]);

        var issues = r.Validate(["CustomerReference", "Dimensions"]).Value;

        issues.ShouldContain(i => i.Contains("PO"));
        issues.ShouldContain(i => i.Contains("Dimensions"));
    }
}

public class InvoiceTests
{
    private static Invoice NewInvoice(decimal taxable)
    {
        var customer = Customer.Create(Guid.NewGuid(), "C1", "Test Customer");
        customer.Update("Test Customer", null, "Line", "Mumbai", "Maharashtra", "400001", null, null, null, null, null, null, null, 30, null, null, null, null, ActiveStatus.Active);
        var invoice = Invoice.Create(customer.TenantId, "INV/26-27/00001", "INV", "26-27", customer, new TaxSettings(TaxMode.Forward, 12, null, null), new DateOnly(2026, 10, 5), 30, null, null);
        var lr = Lr.Create(customer.TenantId, "LR-000001", customer.Id, null, null, Guid.NewGuid(), Guid.NewGuid(), null, null, [("S", "Soap", 1, "PKG", 100, null, null, 1, null, null, null)]);
        var calc = FreightCalculation.Create(customer.TenantId, lr, 1, null, new FreightResult(Guid.NewGuid(), 100, "Actual", taxable, 0, taxable, [], []), null, new LrFacts(new DateOnly(2026, 10, 5), null, null, null, null, null, null, null, 100, null), true, false, [], null, DateTimeOffset.UtcNow, null);
        invoice.AddLine(InvoiceLine.Create(customer.TenantId, invoice.Id, 1, calc, null, "x", TaxCalculator.Compute(taxable, new TaxSettings(TaxMode.Forward, 12, null, null), "27", "27").Value));
        invoice.Issue(null, DateTimeOffset.UtcNow);
        return invoice;
    }

    [Fact]
    public void An_invoice_totals_its_lines_with_tax_and_tracks_what_has_been_paid()
    {
        var invoice = NewInvoice(10000m);

        (invoice.Subtotal, invoice.Cgst, invoice.Sgst, invoice.GrandTotal).ShouldBe((10000m, 600m, 600m, 11200m));
        invoice.PaymentState.ShouldBe(PaymentState.Unpaid);
        invoice.ApplyPayment(5000m).IsSuccess.ShouldBeTrue();
        invoice.PaymentState.ShouldBe(PaymentState.PartiallyPaid);
        invoice.ApplyPayment(6200m).IsSuccess.ShouldBeTrue();
        invoice.PaymentState.ShouldBe(PaymentState.Paid);
        invoice.Outstanding.ShouldBe(0);
    }

    [Fact]
    public void A_payment_larger_than_the_balance_is_a_mismatch_and_changes_nothing()
    {
        var invoice = NewInvoice(1000m);

        var r = invoice.ApplyPayment(5000m);

        r.Error.Code.ShouldBe("ops.payment_mismatch");
        invoice.AmountPaid.ShouldBe(0);
    }

    [Fact]
    public void A_rejected_submission_must_be_resubmitted_and_the_history_is_kept()
    {
        var invoice = NewInvoice(1000m);

        invoice.Submit(null, DateTimeOffset.UtcNow, "PORTAL-1", null, null).IsSuccess.ShouldBeTrue();
        invoice.Submit(null, DateTimeOffset.UtcNow, null, null, null).Error.Code.ShouldBe("ops.invoice_submitted");
        invoice.RejectSubmission(DateTimeOffset.UtcNow, " ").Error.Code.ShouldBe("ops.submission_reason");
        invoice.RejectSubmission(DateTimeOffset.UtcNow, "PO number missing").IsSuccess.ShouldBeTrue();
        invoice.Submission.ShouldBe(SubmissionStatus.ResubmissionRequired);
        invoice.Submit(null, DateTimeOffset.UtcNow, "PORTAL-2", null, null).IsSuccess.ShouldBeTrue();
        invoice.Acknowledge(DateTimeOffset.UtcNow, null).IsSuccess.ShouldBeTrue();

        invoice.Submissions.Select(s => s.Status).ShouldBe([SubmissionStatus.Rejected, SubmissionStatus.Acknowledged]);
    }

    [Fact]
    public void An_invoice_that_was_sent_or_paid_cannot_be_cancelled()
    {
        var sent = NewInvoice(1000m);
        sent.Submit(null, DateTimeOffset.UtcNow, null, null, null);
        sent.Cancel("oops").Error.Code.ShouldBe("ops.invoice_submitted");

        var paid = NewInvoice(1000m);
        paid.ApplyPayment(100m);
        paid.Cancel("oops").Error.Code.ShouldBe("ops.invoice_paid");
    }
}
