using System.Net;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Deliveries.Application;
using Tms.Modules.Deliveries.Application.Claims;
using Tms.Modules.Deliveries.Application.Dashboard;
using Tms.Modules.Deliveries.Application.Notifications;
using Tms.Modules.Deliveries.Domain;
using Tms.Modules.Transporters.Application.Performance;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.DeliveryScenario;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class DeliveryDashboardApiTests(TmsApiFactory factory)
{
    private static async Task AgeAsync(DeliveryScenario s, Guid deliveryId, double hours) =>
        (await s.Admin.PostJsonAsync($"/api/v1/dev/deliveries/{deliveryId}/age", new { hours })).StatusCode.ShouldBe(HttpStatusCode.NoContent);

    private static string Q(DeliveryScenario s) => $"transporterId={s.Transporter.Id}";

    private static async Task<T> GetAsync<T>(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<T>();
    }

    /// <summary>Delivered in full with its proof submitted, which a default tenant accepts by itself.</summary>
    private static async Task<(DeliveryDto Delivery, PodDto Pod)> AcceptedAsync(DeliveryScenario s)
    {
        var arrived = await s.ArrivedAsync();
        var pod = await s.PhotoAndSubmitAsync(await s.CompletedAsync(arrived));
        pod.Summary.Status.ShouldBe(PodStatus.Accepted);
        return (arrived, pod);
    }

    [Fact]
    public async Task The_summary_counts_deliveries_proofs_cases_and_exceptions()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        await AcceptedAsync(s);                                                                   // delivered, accepted
        await s.CompletedAsync(await s.ArrivedAsync());                                          // delivered, proof being prepared
        var failing = await s.ArrivedAsync();
        await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{failing.Summary.Id}/fail", new FailDeliveryRequest("SITE_CLOSED", null, Here()));
        var shortArrived = await s.ArrivedAsync();
        await s.PhotoAndSubmitAsync(await s.CompletedAsync(shortArrived, DeliveryOutcome.Shortage, null, Qty(shortArrived.Items[0], 97, @short: 3)));
        await s.DeliveryAsync();                                                                  // not started

        var summary = await GetAsync<DashboardSummaryDto>(s.Admin, $"/api/v1/pod-dashboard/summary?{Q(s)}");

        summary.Delivered.ShouldBe(1); // the one whose proof is still being prepared; the accepted one has closed
        summary.PartiallyDelivered.ShouldBe(1);
        summary.Failed.ShouldBe(1);
        summary.Closed.ShouldBe(1);
        summary.PodAccepted.ShouldBe(1);
        summary.PodInPreparation.ShouldBe(1);
        summary.PodUnderReview.ShouldBe(1);
        summary.ShortageCases.ShouldBe(1);
        summary.OpenExceptions.ShouldBe(2); // the failure and the shortage
        summary.DeliveriesToday.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Ageing_buckets_stages_and_the_worst_offenders_follow_the_targets()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var late = (await s.ArrivedAsync()); await s.CompletedAsync(late);
        var older = (await s.ArrivedAsync()); await s.CompletedAsync(older);
        var fresh = (await s.ArrivedAsync()); await s.CompletedAsync(fresh);
        await AgeAsync(s, late.Summary.Id, 30);          // past the 24 h target, still "0–1 days"
        await AgeAsync(s, older.Summary.Id, 24 * 5);     // 5 days: "4–7 days"

        var ageing = await GetAsync<AgeingDto>(s.Admin, $"/api/v1/pod-dashboard/ageing?{Q(s)}");

        ageing.BucketLabels.ShouldBe(["0–1 days", "2–3 days", "4–7 days", "8–15 days", "16–30 days", ">30 days"]);
        var submission = ageing.Stages.Single(x => x.Stage == AgeingStage.PendingSubmission);
        (submission.Count, submission.Overdue, submission.TargetHours).ShouldBe((3, 2, 24));
        submission.Buckets.ShouldBe([2, 0, 1, 0, 0, 0]);
        ageing.TopTransporters.Single().Name.ShouldBe(s.Transporter.LegalName);
        ageing.TopTransporters.Single().Overdue.ShouldBe(2);

        var overdue = await GetAsync<PagedResult<AgeingItemDto>>(s.Admin, $"/api/v1/pod-dashboard/ageing/items?{Q(s)}&overdueOnly=true");
        overdue.Items.Select(i => i.DeliveryId).ShouldBe([older.Summary.Id, late.Summary.Id]); // oldest first
        overdue.Items[0].Bucket.ShouldBe("4–7 days");
        var bucket = await GetAsync<PagedResult<AgeingItemDto>>(s.Admin, $"/api/v1/pod-dashboard/ageing/items?{Q(s)}&bucket=2");
        bucket.Items.Single().DeliveryId.ShouldBe(older.Summary.Id);
    }

    [Fact]
    public async Task A_review_taking_too_long_and_a_proof_sent_back_both_age_against_their_own_clock()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var a = await s.ArrivedAsync();
        var waiting = await s.PhotoAndSubmitAsync(await s.CompletedAsync(a, DeliveryOutcome.Shortage, null, Qty(a.Items[0], 97, @short: 3)));
        waiting.Summary.Status.ShouldBe(PodStatus.UnderReview);
        var b = await s.ArrivedAsync();
        var returned = await s.PhotoAndSubmitAsync(await s.CompletedAsync(b, DeliveryOutcome.Shortage, null, Qty(b.Items[0], 97, @short: 3)));
        (await s.Admin.PostJsonAsync($"/api/v1/pods/{returned.Summary.Id}/reject", new ReasonRequest("Wrong site"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        await AgeAsync(s, a.Summary.Id, 5);   // review target is 4 h
        await AgeAsync(s, b.Summary.Id, 13);  // correction target is 12 h

        var ageing = await GetAsync<AgeingDto>(s.Admin, $"/api/v1/pod-dashboard/ageing?{Q(s)}");
        ageing.Stages.Single(x => x.Stage == AgeingStage.PendingReview).Overdue.ShouldBe(1);
        ageing.Stages.Single(x => x.Stage == AgeingStage.Rejected).Overdue.ShouldBe(1);
        ageing.Stages.Single(x => x.Stage == AgeingStage.ResubmissionRequired).Count.ShouldBe(0);
    }

    [Fact]
    public async Task Compliance_is_measured_by_transporter_customer_and_lane_and_says_not_applicable_when_nothing_applies()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var fast = await AcceptedAsync(s);
        var slow = await s.ArrivedAsync(); await s.CompletedAsync(slow);
        await AgeAsync(s, slow.Summary.Id, 30); // proof overdue, never submitted

        var overall = await GetAsync<ComplianceDto>(s.Admin, $"/api/v1/pod-dashboard/compliance?{Q(s)}");
        overall.Rows.Single().Name.ShouldBe(s.Transporter.LegalName);
        var m = overall.Overall;
        (m.Delivered, m.PodSubmitted, m.PodPending, m.PodAccepted).ShouldBe((2, 1, 1, 1));
        m.SubmissionCompliance.ShouldBe(0.5m);
        m.AcceptanceRate.ShouldBe(1m);
        m.RejectionRate.ShouldBe(0m);

        var byCustomer = await GetAsync<ComplianceDto>(s.Admin, $"/api/v1/pod-dashboard/compliance?{Q(s)}&groupBy=customer");
        byCustomer.Rows.Single().Name.ShouldBe("ABC Distributors");
        var byLane = await GetAsync<ComplianceDto>(s.Admin, $"/api/v1/pod-dashboard/compliance?{Q(s)}&groupBy=lane");
        byLane.Rows.Single().Name.ShouldBe("Pune → Surat");

        using var fresh = await DeliveryScenario.CreateAsync(factory);
        var none = await GetAsync<ComplianceDto>(fresh.Admin, $"/api/v1/pod-dashboard/compliance?{Q(fresh)}");
        none.Overall.SubmissionCompliance.ShouldBeNull();
        none.Overall.AcceptanceRate.ShouldBeNull();
        none.Rows.ShouldBeEmpty();
        fast.Pod.Summary.Status.ShouldBe(PodStatus.Accepted);
    }

    [Fact]
    public async Task A_transporter_sees_the_dashboard_for_its_own_company_only()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        await AcceptedAsync(s);
        using var other = await DeliveryScenario.CreateAsync(factory);
        await AcceptedAsync(other);

        var mine = await GetAsync<ComplianceDto>(s.Vendor, $"/api/v1/pod-dashboard/compliance?transporterId={other.Transporter.Id}"); // asking for someone else's is ignored
        mine.Overall.Delivered.ShouldBe(1);
        mine.Rows.Single().Name.ShouldBe(s.Transporter.LegalName);
        (await s.Vendor.GetAsync("/api/v1/delivery-reports/deliveries")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        using var nobody = await factory.UserWithPermissionsAsync(s.Admin, "users.read");
        (await nobody.GetAsync("/api/v1/pod-dashboard/summary")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Overdue_and_returned_proofs_notify_the_right_people_once()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var late = await s.ArrivedAsync(); await s.CompletedAsync(late);
        await AgeAsync(s, late.Summary.Id, 30);

        var vendor = await GetAsync<PagedResult<NotificationDto>>(s.Vendor, "/api/v1/delivery-notifications?pageSize=100");
        var overdue = vendor.Items.Single(n => n.DeliveryId == late.Summary.Id);
        overdue.Kind.ShouldBe(NotificationKind.PodOverdue);
        overdue.Title.ShouldContain(late.Summary.Number);
        overdue.Read.ShouldBeFalse();
        var staff = await GetAsync<PagedResult<NotificationDto>>(s.Admin, "/api/v1/delivery-notifications?pageSize=100");
        staff.Items.ShouldContain(n => n.DeliveryId == late.Summary.Id && n.Kind == NotificationKind.PodOverdue);

        // Reading again finds the same condition and says nothing new.
        var again = await GetAsync<PagedResult<NotificationDto>>(s.Vendor, "/api/v1/delivery-notifications?pageSize=100");
        again.Items.Count(n => n.DeliveryId == late.Summary.Id).ShouldBe(1);

        // A rival company never sees it.
        var rival = await GetAsync<PagedResult<NotificationDto>>(s.Rival, "/api/v1/delivery-notifications?pageSize=100");
        rival.Items.ShouldNotContain(n => n.DeliveryId == late.Summary.Id);

        // Read state is the reader's own.
        (await s.Vendor.PostAsync($"/api/v1/delivery-notifications/{overdue.Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await GetAsync<PagedResult<NotificationDto>>(s.Vendor, "/api/v1/delivery-notifications?unreadOnly=true&pageSize=100")).Items.ShouldNotContain(n => n.Id == overdue.Id);
        (await GetAsync<PagedResult<NotificationDto>>(s.Admin, "/api/v1/delivery-notifications?unreadOnly=true&pageSize=100")).Items.ShouldContain(n => n.DeliveryId == late.Summary.Id);
        (await s.Rival.PostAsync($"/api/v1/delivery-notifications/{overdue.Id}/read", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_rejection_a_refusal_a_shortage_and_an_escalation_each_tell_the_people_who_must_act()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var a = await s.ArrivedAsync();
        var pod = await s.PhotoAndSubmitAsync(await s.CompletedAsync(a, DeliveryOutcome.Shortage, null, Qty(a.Items[0], 97, @short: 3)));
        await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/reject", new ReasonRequest("Photo is of the wrong site"));
        var refused = await s.ArrivedAsync();
        await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{refused.Summary.Id}/refuse", new RefuseDeliveryRequest("WRONG_ITEM", "Anil", null, true, Here()));

        var vendor = (await GetAsync<PagedResult<NotificationDto>>(s.Vendor, "/api/v1/delivery-notifications?pageSize=100")).Items;
        vendor.ShouldContain(n => n.Kind == NotificationKind.PodRejected && n.DeliveryId == a.Summary.Id && n.Body == "Photo is of the wrong site");

        var staff = (await GetAsync<PagedResult<NotificationDto>>(s.Admin, "/api/v1/delivery-notifications?pageSize=100")).Items;
        staff.ShouldContain(n => n.Kind == NotificationKind.ShortageRecorded && n.DeliveryId == a.Summary.Id);
        staff.ShouldContain(n => n.Kind == NotificationKind.CustomerRefusal && n.DeliveryId == refused.Summary.Id);

        var exception = (await GetAsync<PagedResult<ExceptionSummaryDto>>(s.Admin, $"/api/v1/delivery-exceptions?deliveryId={refused.Summary.Id}")).Items.Single();
        await s.Admin.PostJsonAsync($"/api/v1/delivery-exceptions/{exception.Id}/escalate", new ReasonRequest("Key account"));
        (await GetAsync<PagedResult<NotificationDto>>(s.Admin, "/api/v1/delivery-notifications?pageSize=100")).Items.ShouldContain(n => n.Kind == NotificationKind.ExceptionEscalated && n.ExceptionId == exception.Id);
    }

    [Fact]
    public async Task Reports_come_as_csv_or_excel_and_are_safe_to_open()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var request = s.Request() with { CustomerName = "=HYPERLINK(\"http://evil\")" };
        var arrived = await s.ArrivedAsync(request);
        await s.CompletedAsync(arrived, DeliveryOutcome.Shortage, null, Qty(arrived.Items[0], 97, @short: 3));

        var csv = await s.Admin.GetAsync($"/api/v1/delivery-reports/deliveries?{Q(s)}&format=csv");
        csv.StatusCode.ShouldBe(HttpStatusCode.OK);
        csv.Content.Headers.ContentType!.MediaType.ShouldBe("text/csv");
        var text = await csv.Content.ReadAsStringAsync();
        text.ShouldContain("Delivery,Shipment,Customer");
        text.ShouldContain(arrived.Summary.Number);
        text.ShouldContain("'=HYPERLINK"); // a cell never starts with = so Excel will not run it
        text.ShouldNotContain(",=HYPERLINK");

        var xlsx = await s.Admin.GetAsync($"/api/v1/delivery-reports/shortages?{Q(s)}&format=xlsx");
        xlsx.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await xlsx.Content.ReadAsByteArrayAsync();
        (bytes[0], bytes[1]).ShouldBe(((byte)'P', (byte)'K')); // an Excel file is a zip
        var shortages = await (await s.Admin.GetAsync($"/api/v1/delivery-reports/shortages?{Q(s)}")).Content.ReadAsStringAsync();
        shortages.ShouldContain("SHORT_LOADED");

        foreach (var report in ReportsHandler.Reports)
        {
            (await s.Admin.GetAsync($"/api/v1/delivery-reports/{report}?{Q(s)}")).StatusCode.ShouldBe(HttpStatusCode.OK, report);
        }

        (await s.Admin.GetAsync("/api/v1/delivery-reports/deliveries?format=pdf")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.GetAsync("/api/v1/delivery-reports/nonsense")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_claim_is_raised_from_the_discrepancy_with_everything_filled_in_and_never_twice()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();
        var item = arrived.Items[0];
        var started = await s.CompletedAsync(arrived, DeliveryOutcome.Shortage, null, Qty(item, 95, @short: 3, damaged: 2));
        (await UploadAsync(s.Vendor, started.Summary.Id, TestImages.Jpeg(seed: Random.Shared.Next()), EvidenceType.DamagePhoto)).StatusCode.ShouldBe(HttpStatusCode.Created);
        await s.PhotoAndSubmitAsync(started);

        (await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{arrived.Summary.Id}/claims", new CreateClaimsRequest(null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var made = await s.Admin.PostJsonAsync($"/api/v1/deliveries/{arrived.Summary.Id}/claims", new CreateClaimsRequest(null));
        made.StatusCode.ShouldBe(HttpStatusCode.OK, await made.Content.ReadAsStringAsync());
        var claims = await made.ReadAsync<List<ClaimResultDto>>();

        claims.Select(c => (c.Type, c.Quantity)).ShouldBe([(DiscrepancyType.Shortage, 3m), (DiscrepancyType.Damage, 2m)], ignoreOrder: true);
        claims.ShouldAllBe(c => c.Reference.StartsWith("CLM-L-", StringComparison.Ordinal) && c.System == "local");
        var after = await GetAsync<DeliveryDto>(s.Admin, $"/api/v1/deliveries/{arrived.Summary.Id}");
        after.Discrepancies.ShouldAllBe(d => d.ClaimReference != null);
        var exceptions = (await GetAsync<PagedResult<ExceptionSummaryDto>>(s.Admin, $"/api/v1/delivery-exceptions?deliveryId={arrived.Summary.Id}")).Items;
        exceptions.ShouldAllBe(e => e.ClaimReference != null);

        (await s.Admin.PostJsonAsync($"/api/v1/deliveries/{arrived.Summary.Id}/claims", new CreateClaimsRequest(null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_tenant_can_have_claims_raised_automatically_when_the_proof_is_accepted()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var original = DeliverySettingDefaults.For(DeliverySettingKeys.Discrepancy)!;
        await s.SetSettingAsync(DeliverySettingKeys.Discrepancy, new DiscrepancyRulesSetting(false, false, false, true));
        try
        {
            var arrived = await s.ArrivedAsync();
            var pod = await s.PhotoAndSubmitAsync(await s.CompletedAsync(arrived, DeliveryOutcome.Shortage, null, Qty(arrived.Items[0], 97, @short: 3)));
            (await s.Admin.PostAsync($"/api/v1/pods/{pod.Summary.Id}/approve", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

            DeliveryDto? delivery = null;
            for (var i = 0; i < 50 && delivery?.Discrepancies.All(d => d.ClaimReference != null) != true; i++)
            {
                await Task.Delay(100);
                delivery = await GetAsync<DeliveryDto>(s.Admin, $"/api/v1/deliveries/{arrived.Summary.Id}");
            }

            delivery!.Discrepancies.ShouldAllBe(d => d.ClaimReference != null);
        }
        finally
        {
            await s.SetSettingAsync(DeliverySettingKeys.Discrepancy, original);
        }
    }

    [Fact]
    public async Task Freight_audit_is_told_where_each_proof_stands_and_the_invoice_is_held_until_it_is_accepted()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var arrived = await s.ArrivedAsync();
        var pod = await s.CompletedAsync(arrived, DeliveryOutcome.Shortage, null, Qty(arrived.Items[0], 97, @short: 3));

        var pending = await Billing(s, arrived.Summary.Id, "Pending");
        (pending.BillingEligible, pending.InvoiceHold).ShouldBe((false, true));

        await s.PhotoAndSubmitAsync(pod);
        (await Billing(s, arrived.Summary.Id, "Submitted")).InvoiceHold.ShouldBeTrue();

        await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/reject", new ReasonRequest("Wrong site"));
        var rejected = await Billing(s, arrived.Summary.Id, "Rejected");
        (rejected.BillingEligible, rejected.InvoiceHold).ShouldBe((false, true));
        rejected.Explanation.ShouldContain("rejected");

        await UploadAsync(s.Vendor, pod.Summary.Id, TestImages.Jpeg(seed: Random.Shared.Next()));
        await s.Vendor.PostAsync($"/api/v1/pods/{pod.Summary.Id}/resubmit", null);
        await s.Admin.PostAsync($"/api/v1/pods/{pod.Summary.Id}/approve", null);
        var accepted = await Billing(s, arrived.Summary.Id, "Accepted");
        (accepted.BillingEligible, accepted.InvoiceHold).ShouldBe((true, false));
        (await s.Rival.GetAsync($"/api/v1/deliveries/{arrived.Summary.Id}/billing")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_tenant_that_does_not_hold_invoices_makes_a_completed_delivery_billable_at_once()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var original = DeliverySettingDefaults.For(DeliverySettingKeys.Billing)!;
        await s.SetSettingAsync(DeliverySettingKeys.Billing, new BillingSetting(false));
        try
        {
            var arrived = await s.ArrivedAsync();
            await s.CompletedAsync(arrived);
            var status = await Billing(s, arrived.Summary.Id, "Pending");
            (status.BillingEligible, status.InvoiceHold).ShouldBe((true, false));
        }
        finally
        {
            await s.SetSettingAsync(DeliverySettingKeys.Billing, original);
        }
    }

    private static async Task<BillingStatusDto> Billing(DeliveryScenario s, Guid deliveryId, string expected)
    {
        BillingStatusDto? status = null;
        for (var i = 0; i < 50 && status?.Status != expected; i++)
        {
            await Task.Delay(100);
            status = await GetAsync<BillingStatusDto>(s.Admin, $"/api/v1/deliveries/{deliveryId}/billing");
        }

        status!.Status.ShouldBe(expected);
        return status;
    }

    [Fact]
    public async Task The_transporters_record_shows_how_its_proofs_have_gone()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        await AcceptedAsync(s);
        var shortArrived = await s.ArrivedAsync();
        var pod = await s.PhotoAndSubmitAsync(await s.CompletedAsync(shortArrived, DeliveryOutcome.Shortage, null, Qty(shortArrived.Items[0], 97, @short: 3)));
        await s.Admin.PostJsonAsync($"/api/v1/pods/{pod.Summary.Id}/reject", new ReasonRequest("Wrong site"));
        var refused = await s.ArrivedAsync();
        await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{refused.Summary.Id}/refuse", new RefuseDeliveryRequest("WRONG_ITEM", "Anil", null, true, Here()));

        ProofPerformanceDto? perf = null;
        for (var i = 0; i < 50 && perf is not { Deliveries: 3, Refusals: 1 }; i++)
        {
            await Task.Delay(100);
            perf = await GetAsync<ProofPerformanceDto>(s.Admin, $"/api/v1/transporters/{s.Transporter.Id}/proof-performance");
        }

        perf!.Deliveries.ShouldBe(3);
        perf.Delivered.ShouldBe(2);
        perf.Refusals.ShouldBe(1);
        perf.FirstTimeAcceptanceRate.ShouldBe(1m); // the one that was accepted, was accepted first time
        perf.RejectionRate.ShouldBe(0.5m); // one accepted, one rejected
        perf.ShortageRate.ShouldBe(0.5m);
        perf.OnTimeRate.ShouldBe(1m);
        (await s.Rival.GetAsync($"/api/v1/transporters/{s.Transporter.Id}/proof-performance")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        using var fresh = await DeliveryScenario.CreateAsync(factory);
        var none = await GetAsync<ProofPerformanceDto>(fresh.Admin, $"/api/v1/transporters/{fresh.Transporter.Id}/proof-performance");
        (none.Deliveries, none.OnTimeRate, none.RejectionRate).ShouldBe((0, null, null));
    }

    [Fact]
    public async Task Planning_can_ask_how_dependable_a_lane_has_been()
    {
        using var s = await DeliveryScenario.CreateAsync(factory);
        var origin = $"Origin{Guid.NewGuid():N}"[..14];
        var destination = $"Dest{Guid.NewGuid():N}"[..12];
        async Task<DeliveryDto> OnLaneAsync()
        {
            var created = await s.Admin.PostJsonAsync("/api/v1/deliveries", s.Request() with { OriginReference = origin, DestinationReference = destination });
            return await created.ReadAsync<DeliveryDto>();
        }

        var good = await OnLaneAsync();
        await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{good.Summary.Id}/start", Here());
        await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{good.Summary.Id}/arrive", Here());
        await s.CompleteAsync(good, Complete(DeliveryOutcome.Full, null, null, Qty(good.Items[0], 100)));
        var failed = await OnLaneAsync();
        await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{failed.Summary.Id}/start", Here());
        await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{failed.Summary.Id}/arrive", Here());
        await s.Vendor.PostJsonAsync($"/api/v1/deliveries/{failed.Summary.Id}/fail", new FailDeliveryRequest("SITE_CLOSED", null, Here()));

        var lane = await GetAsync<LaneReliability>(s.Admin, $"/api/v1/delivery-reliability?origin={origin}&destination={destination}&days=30");

        (lane.Deliveries, lane.FailureRate, lane.RefusalRate, lane.OnTimeRate).ShouldBe((2, 0.5m, 0m, 1m));
        var unknown = await GetAsync<LaneReliability>(s.Admin, "/api/v1/delivery-reliability?origin=Nowhere&destination=Nothing");
        (unknown.Deliveries, unknown.OnTimeRate, unknown.FailureRate).ShouldBe((0, null, null));
        (await s.Vendor.GetAsync($"/api/v1/delivery-reliability?origin={origin}&destination={destination}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid podId, byte[] bytes, EvidenceType type = EvidenceType.PackagePhoto) =>
        DeliveryScenario.UploadAsync(client, podId, bytes, type, null, "image/jpeg");
}
