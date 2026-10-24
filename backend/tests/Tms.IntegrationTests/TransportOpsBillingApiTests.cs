using System.Net;
using System.Text.RegularExpressions;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Approvals.Application;
using Tms.Modules.TransportOps.Application;
using Tms.Modules.TransportOps.Domain;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransportOpsFreightApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task Freight_is_worked_out_from_the_customers_agreement_and_explained_line_by_line()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var (lr, _) = await s.DeliveredAsync(TimeSpan.FromHours(3)); // within the 4 free hours
        await s.AcceptedPodAsync(lr.Lr.Lr.Id);

        var calc = await s.CalculateAsync(lr.Lr.Lr.Id);

        calc.Status.ShouldBe(CalculationStatus.Calculated);
        (calc.BillableWeightKg, calc.BasicFreight, calc.DetentionAmount, calc.TotalPreTax).ShouldBe((1200m, 4200m, 0m, 4200m)); // 1200 kg x 3.50
        calc.AgreementId.ShouldBe(s.Agreement.Id);
        calc.AgreementRevision.ShouldBe(1);
        calc.PodAccepted.ShouldBeTrue();
        calc.Lines.Single().Code.ShouldBe("BASE_FREIGHT");
        calc.Trace.ShouldContain(t => t.Contains(s.Agreement.Number));
        calc.Detention!.Status.ShouldBe(DetentionStatus.Calculated);
    }

    [Fact]
    public async Task Detention_beyond_the_free_time_is_a_separate_line_with_its_own_breakdown()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var (lr, _) = await s.DeliveredAsync(TimeSpan.FromHours(6).Add(TimeSpan.FromMinutes(10))); // 2h10 over; the rule rounds up to 3 hours at 200
        await s.AcceptedPodAsync(lr.Lr.Lr.Id);

        var calc = await s.CalculateAsync(lr.Lr.Lr.Id);

        calc.DetentionAmount.ShouldBe(600m);
        calc.TotalPreTax.ShouldBe(4200m + 600m);
        calc.Lines.Select(l => l.Code).ShouldBe(["BASE_FREIGHT", "DETENTION"]);
        calc.Detention!.Units.ShouldBe(3);
        calc.Detention.ChargeableHours.ShouldBeInRange(2.1, 2.2);
        calc.Detention.Arrival.ShouldNotBeNull();
        calc.Detention.Unloading.ShouldNotBeNull();
        calc.Trace.ShouldContain(t => t.Contains("Detention 600.00"));
    }

    [Fact]
    public async Task Detention_follows_each_customers_own_rule()
    {
        // Free until midnight (IST) of the arrival day, then a flat 1,500 per started day, capped at 3,000.
        var rule = new DetentionPolicy(true, FreeTimeKind.EndOfDay, 0, DetentionUnit.Hour, DetentionUnit.Day, 1500, PartUnitRounding.RoundUp, 3000, null);
        using var s = await OpsScenario.CreateAsync(factory, detention: rule);
        var (lr, _) = await s.DeliveredAsync(TimeSpan.FromHours(96), departed: DateTimeOffset.UtcNow.AddDays(-10)); // a long wait: 3+ started days beyond the arrival day, so the cap always bites
        await s.AcceptedPodAsync(lr.Lr.Lr.Id);

        var calc = await s.CalculateAsync(lr.Lr.Lr.Id);

        calc.Detention!.CapApplied.ShouldBeTrue();
        calc.DetentionAmount.ShouldBe(3000m);

        using var other = await OpsScenario.CreateAsync(factory, detention: DetentionPolicy.None);
        var (lr2, _) = await other.DeliveredAsync(TimeSpan.FromHours(96), departed: DateTimeOffset.UtcNow.AddDays(-10));
        await other.AcceptedPodAsync(lr2.Lr.Lr.Id);
        var none = await other.CalculateAsync(lr2.Lr.Lr.Id);
        none.DetentionAmount.ShouldBe(0);
        none.Detention!.Status.ShouldBe(DetentionStatus.NotApplicable);
        none.Status.ShouldBe(CalculationStatus.Calculated); // not applicable is not a hold
    }

    [Fact]
    public async Task On_an_actual_weight_basis_the_volume_is_ignored_and_the_rate_is_applied_to_the_weight()
    {
        using var s = await OpsScenario.CreateAsync(factory, (r, rates) => { rates[0] = rates[0] with { Rate = 2m }; });
        var (lr, _) = await s.DeliveredAsync(TimeSpan.FromHours(1), kg: 1000, cbm: 8);
        await s.AcceptedPodAsync(lr.Lr.Lr.Id);

        var calc = await s.CalculateAsync(lr.Lr.Lr.Id);

        calc.BillableWeightKg.ShouldBe(1000m);
        calc.BasicFreight.ShouldBe(2000m);
    }

    [Fact]
    public async Task A_delivery_whose_pod_is_not_accepted_is_held_and_released_by_recalculating_after_acceptance()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var (lr, _) = await s.DeliveredAsync(TimeSpan.FromHours(1));

        var held = await s.CalculateAsync(lr.Lr.Lr.Id);
        held.Status.ShouldBe(CalculationStatus.OnHold);
        held.Holds.ShouldContain(h => h.Contains("proof of delivery"));
        held.BasicFreight.ShouldBe(4200m); // the amounts are shown, but cannot be billed
        (await s.Admin.PostJsonAsync($"/api/v1/ops/billing/calculations/{held.Id}/review", new ReviewCalculationRequest(null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([held.Id], null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        await s.AcceptedPodAsync(lr.Lr.Lr.Id);
        var again = await s.CalculateAsync(lr.Lr.Lr.Id);

        again.Status.ShouldBe(CalculationStatus.Calculated);
        again.Version.ShouldBe(held.Version + 1);
        var history = await OpsScenario.Ok<List<CalculationDto>>(s.Admin.GetAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/calculations"));
        history.Select(c => c.Status).ShouldBe([CalculationStatus.Calculated, CalculationStatus.Superseded]); // newest first; the old one is kept, not counted
    }

    [Fact]
    public async Task Recalculating_never_duplicates_a_charge_and_gives_the_same_figures()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var (lr, _) = await s.DeliveredAsync(TimeSpan.FromHours(7));
        await s.AcceptedPodAsync(lr.Lr.Lr.Id);

        var first = await s.CalculateAsync(lr.Lr.Lr.Id);
        var second = await s.CalculateAsync(lr.Lr.Lr.Id);
        var third = await s.CalculateAsync(lr.Lr.Lr.Id);

        (second.TotalPreTax, third.TotalPreTax).ShouldBe((first.TotalPreTax, first.TotalPreTax));
        var rows = await OpsScenario.Ok<List<CalculationDto>>(s.Admin.GetAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/calculations"));
        rows.Count(c => c.Status != CalculationStatus.Superseded).ShouldBe(1);
        rows.Select(c => c.Version).OrderBy(v => v).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task A_lane_without_a_rate_or_a_customer_without_an_agreement_is_held_with_the_reason()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var elsewhere = await OpsScenario.Ok<LocationDto>(s.Admin.PostJsonAsync("/api/v1/ops/locations", new SaveLocationRequest($"SUR{Guid.NewGuid().ToString("N")[..6]}", "Surat Distributor", LocationKind.Distributor, s.Customer.Id,
            "Ring Road", "Surat", "Gujarat", null, 21.17, 72.83, 150, null, ActiveStatus.Active)));
        var (noRate, _) = await s.DeliveredAsync(TimeSpan.FromHours(1), elsewhere);
        await s.AcceptedPodAsync(noRate.Lr.Lr.Id);

        var held = await s.CalculateAsync(noRate.Lr.Lr.Id);

        held.Status.ShouldBe(CalculationStatus.OnHold);
        held.Holds.ShouldContain(h => h.Contains("No rate"));
        held.TotalPreTax.ShouldBe(0);

        var (future, _) = await s.DeliveredAsync(TimeSpan.FromHours(1), date: new DateOnly(2030, 1, 1));
        await s.AcceptedPodAsync(future.Lr.Lr.Id);
        (await s.CalculateAsync(future.Lr.Lr.Id)).Holds.ShouldContain(h => h.Contains("no agreement"));
    }

    [Fact]
    public async Task An_lr_that_is_not_delivered_cannot_be_priced()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var lr = await s.CreateLrAsync();

        var result = await OpsScenario.Ok<CalculateResultDto>(s.Admin.PostJsonAsync("/api/v1/ops/billing/calculate", new CalculateRequest([lr.Lr.Lr.Id])));

        result.Calculations.ShouldBeEmpty();
        result.Failures.Single().Code.ShouldBe("ops.billing_not_delivered");
    }

    [Fact]
    public async Task Billing_without_an_accepted_pod_is_possible_only_when_the_agreement_allows_it_and_it_is_recorded()
    {
        using var s = await OpsScenario.CreateAsync(factory, billing: new BillingRules(true, true, "Customer pays on dispatch advice"));
        var (lr, _) = await s.DeliveredAsync(TimeSpan.FromHours(1));

        var calc = await s.CalculateAsync(lr.Lr.Lr.Id);

        calc.Status.ShouldBe(CalculationStatus.Calculated);
        calc.PodAccepted.ShouldBeFalse();
        calc.BilledWithoutPod.ShouldBeTrue(); // the exception is on the record
    }

    [Fact]
    public async Task Rates_are_effective_dated_a_new_revision_prices_later_deliveries_and_the_old_terms_still_price_the_old_ones()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // An active agreement cannot be edited.
        var edit = await s.Admin.PutJsonAsync($"/api/v1/ops/agreements/{s.Agreement.Id}", new SaveAgreementRequest(s.Customer.Id, "x", s.Agreement.EffectiveFrom, s.Agreement.EffectiveTo, s.Agreement.WeightBasis, null, null,
            s.Agreement.Detention, s.Agreement.Tax, s.Agreement.Billing, [], s.Agreement.Rates, s.Agreement.Version));
        edit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await edit.ProblemCodeAsync()).ShouldBe("ops.agreement_locked");

        var draft = await OpsScenario.Ok<AgreementDto>(s.Admin.PostJsonAsync($"/api/v1/ops/agreements/{s.Agreement.Id}/revise", new ReviseAgreementRequest(today.AddDays(10))));
        draft.Revision.ShouldBe(2);
        var newRates = draft.Rates.Select(r => r.Method == RateMethod.PerKg ? r with { Rate = 4.5m } : r).ToList();
        var edited = await OpsScenario.Ok<AgreementDto>(s.Admin.PutJsonAsync($"/api/v1/ops/agreements/{draft.Id}", new SaveAgreementRequest(s.Customer.Id, draft.Title, draft.EffectiveFrom, draft.EffectiveTo, draft.WeightBasis,
            draft.VolumetricKgPerCbm, draft.MinimumFreight, draft.Detention, draft.Tax, draft.Billing, draft.ExtraCharges, newRates, draft.Version)));
        await OpsScenario.Ok<AgreementDto>(s.Admin.PostAsync($"/api/v1/ops/agreements/{edited.Id}/activate", null));

        var (oldLr, _) = await s.DeliveredAsync(TimeSpan.FromHours(1), date: today);
        var (newLr, _) = await s.DeliveredAsync(TimeSpan.FromHours(1), date: today.AddDays(20));
        await s.AcceptedPodAsync(oldLr.Lr.Lr.Id);
        await s.AcceptedPodAsync(newLr.Lr.Lr.Id);

        var old = await s.CalculateAsync(oldLr.Lr.Lr.Id);
        var fresh = await s.CalculateAsync(newLr.Lr.Lr.Id);

        (old.AgreementRevision, old.BasicFreight).ShouldBe((1, 4200m)); // 1200 x 3.50, the terms of its own date
        (fresh.AgreementRevision, fresh.BasicFreight).ShouldBe((2, 5400m)); // 1200 x 4.50
        var list = await OpsScenario.Ok<List<AgreementSummaryDto>>(s.Admin.GetAsync($"/api/v1/ops/agreements?customerId={s.Customer.Id}"));
        list.Select(a => (a.Revision, a.Status)).OrderBy(x => x.Revision).ShouldBe([(1, AgreementStatus.Superseded), (2, AgreementStatus.Active)]);
    }

    [Fact]
    public async Task An_agreement_with_two_rates_for_the_same_lane_or_overlapping_dates_is_refused_when_saved_or_activated()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var duplicate = s.Agreement.Rates.Concat([s.Agreement.Rates[0]]).ToList();
        var response = await s.Admin.PostJsonAsync("/api/v1/ops/agreements", new SaveAgreementRequest(s.Customer.Id, "Dup", new DateOnly(2027, 4, 1), null, WeightBasis.Actual, null, null, DetentionPolicy.None, TaxSettings.None, BillingRules.Standard, [], duplicate));
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var overlapping = await OpsScenario.Ok<AgreementDto>(s.Admin.PostJsonAsync("/api/v1/ops/agreements", new SaveAgreementRequest(s.Customer.Id, "Second agreement", new DateOnly(2026, 6, 1), new DateOnly(2026, 12, 31),
            WeightBasis.Actual, null, null, DetentionPolicy.None, TaxSettings.None, BillingRules.Standard, [], s.Agreement.Rates)));
        var activate = await s.Admin.PostAsync($"/api/v1/ops/agreements/{overlapping.Id}/activate", null);
        activate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await activate.ProblemCodeAsync()).ShouldBe("ops.agreement_overlap");
    }

    [Fact]
    public async Task The_billing_workbench_lists_delivered_lrs_with_their_pod_and_charges_and_filters_them()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var (a, _) = await s.DeliveredAsync(TimeSpan.FromHours(1));
        var (b, _) = await s.DeliveredAsync(TimeSpan.FromHours(1));
        await s.AcceptedPodAsync(a.Lr.Lr.Id);
        await s.CalculateAsync(a.Lr.Lr.Id);

        var rows = await OpsScenario.Ok<PagedResult<BillingRowDto>>(s.Admin.GetAsync($"/api/v1/ops/billing?customerId={s.Customer.Id}"));

        rows.Items.Count.ShouldBe(2);
        var ready = rows.Items.Single(r => r.LrId == a.Lr.Lr.Id);
        (ready.PodStatus, ready.CalcStatus, ready.TotalPreTax, ready.Eligible).ShouldBe((PodStatus.Accepted, CalculationStatus.Calculated, 4200m, false)); // calculated, not yet reviewed
        var waiting = rows.Items.Single(r => r.LrId == b.Lr.Lr.Id);
        waiting.CalcId.ShouldBeNull();
        waiting.Holds.ShouldContain(h => h.Contains("POD"));
        var onlyPod = await OpsScenario.Ok<PagedResult<BillingRowDto>>(s.Admin.GetAsync($"/api/v1/ops/billing?customerId={s.Customer.Id}&podStatus=Accepted"));
        onlyPod.Items.Select(r => r.LrId).ShouldBe([a.Lr.Lr.Id]);
    }
}

[Collection(ApiCollection.Name)]
public class TransportOpsInvoiceApiTests(TmsApiFactory factory)
{
    private static async Task<(OpsScenario S, Guid LrId, CalculationDto Calc)> ReviewedDeliveryAsync(TmsApiFactory factory, TimeSpan? waited = null, OpsScenario? existing = null)
    {
        var s = existing ?? await OpsScenario.CreateAsync(factory);
        var (lr, _) = await s.DeliveredAsync(waited ?? TimeSpan.FromHours(7));
        await s.AcceptedPodAsync(lr.Lr.Lr.Id);
        return (s, lr.Lr.Lr.Id, await s.ReviewedAsync(lr.Lr.Lr.Id));
    }

    private static long Sequence(string number) => long.Parse(number.Split('/')[2], System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task An_invoice_has_one_line_per_delivery_with_freight_and_detention_apart_and_tax_from_the_agreement()
    {
        var (s, lrId, calc) = await ReviewedDeliveryAsync(factory); // 7 hours: 3 over the free 4 -> detention 600
        using var _s = s;

        var invoice = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, "Oct 2026")));

        Regex.IsMatch(invoice.Summary.Number, @"^INV/\d{2}-\d{2}/\d{5}$").ShouldBeTrue(invoice.Summary.Number);
        invoice.Summary.Status.ShouldBe(InvoiceStatus.Issued);
        var line = invoice.Lines.Single();
        (line.BasicFreight, line.Detention, line.OtherCharges, line.Taxable).ShouldBe((4200m, 600m, 0m, 4800m));
        (line.Cgst, line.Sgst, line.Igst).ShouldBe((288m, 288m, 0m)); // 12% of 4,800 within Maharashtra
        (invoice.Summary.Subtotal, invoice.Summary.Tax, invoice.Summary.GrandTotal).ShouldBe((4800m, 576m, 5376m));
        (line.LrId, line.FreightCalculationId, line.CalculationVersion, line.AgreementRevision).ShouldBe((lrId, calc.Id, calc.Version, 1));
        invoice.Sac.ShouldBe("996511");
        invoice.CustomerGstin.ShouldBe(OpsScenario.CustomerGstin);
        invoice.Summary.DueDate.ShouldBe(invoice.Summary.InvoiceDate.AddDays(30));
        (await OpsScenario.Ok<CalculationDto>(s.Admin.GetAsync($"/api/v1/ops/billing/calculations/{calc.Id}"))).Status.ShouldBe(CalculationStatus.Invoiced);
    }

    [Fact]
    public async Task Only_reviewed_unbilled_deliveries_of_one_customer_can_be_invoiced_and_never_twice()
    {
        var (s, _, calc) = await ReviewedDeliveryAsync(factory);
        using var _s = s;
        var (lr2, _) = await s.DeliveredAsync(TimeSpan.FromHours(1));
        await s.AcceptedPodAsync(lr2.Lr.Lr.Id);
        var unreviewed = await s.CalculateAsync(lr2.Lr.Lr.Id);

        (await s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([unreviewed.Id], null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([], null, null, null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null)));
        var again = await s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("ops.invoice_duplicate");
        using var other = await OpsScenario.CreateAsync(factory);
        var (_, _, otherCalc) = await ReviewedDeliveryAsync(factory, existing: other);
        var (lr3, _) = await s.DeliveredAsync(TimeSpan.FromHours(1));
        await s.AcceptedPodAsync(lr3.Lr.Lr.Id);
        var mine = await s.ReviewedAsync(lr3.Lr.Lr.Id);
        var mixed = await s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([mine.Id, otherCalc.Id], null, null, null));
        mixed.StatusCode.ShouldBe(HttpStatusCode.BadRequest); // one invoice is for one customer
    }

    [Fact]
    public async Task Several_deliveries_can_share_one_invoice_and_the_numbers_have_no_gaps_even_after_a_failed_attempt()
    {
        var (s, _, first) = await ReviewedDeliveryAsync(factory);
        using var _s = s;
        var (_, _, second) = await ReviewedDeliveryAsync(factory, existing: s);
        var (_, _, third) = await ReviewedDeliveryAsync(factory, existing: s);

        var one = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([first.Id, second.Id], null, null, null)));
        one.Lines.Count.ShouldBe(2);
        one.Summary.Subtotal.ShouldBe(one.Lines.Sum(l => l.Taxable));

        // A failure after the number was taken (tax cannot be worked out without the supplier's state) rolls the number back with everything else.
        (await s.Admin.PutJsonAsync("/api/v1/ops/settings", new OpsSettingsDto(new ArrivalSetting(), new NumberingSetting(), new PodSetting(), new InvoiceSetting("INV", null, null, false), new FleetSetting()))).EnsureSuccessStatusCode();
        var failed = await s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([third.Id], null, null, null));
        failed.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await failed.ProblemCodeAsync()).ShouldBe("OPS_TAX_PLACE_OF_SUPPLY");
        (await OpsScenario.Ok<CalculationDto>(s.Admin.GetAsync($"/api/v1/ops/billing/calculations/{third.Id}"))).Status.ShouldBe(CalculationStatus.Reviewed); // nothing was half-done
        (await s.Admin.PutJsonAsync("/api/v1/ops/settings", new OpsSettingsDto(new ArrivalSetting(), new NumberingSetting(), new PodSetting(), new InvoiceSetting("INV", null, "27", false), new FleetSetting()))).EnsureSuccessStatusCode();

        var next = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([third.Id], null, null, null)));

        Sequence(next.Summary.Number).ShouldBe(Sequence(one.Summary.Number) + 1);
    }

    [Fact]
    public async Task An_issued_invoice_does_not_change_when_the_agreement_is_revised_later()
    {
        var (s, _, calc) = await ReviewedDeliveryAsync(factory);
        using var _s = s;
        var invoice = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null)));

        var draft = await OpsScenario.Ok<AgreementDto>(s.Admin.PostJsonAsync($"/api/v1/ops/agreements/{s.Agreement.Id}/revise", new ReviseAgreementRequest(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5))));
        var pricier = draft.Rates.Select(r => r with { Rate = r.Rate * 2 }).ToList();
        await OpsScenario.Ok<AgreementDto>(s.Admin.PutJsonAsync($"/api/v1/ops/agreements/{draft.Id}", new SaveAgreementRequest(s.Customer.Id, draft.Title, draft.EffectiveFrom, null, draft.WeightBasis, null, null, draft.Detention,
            draft.Tax, draft.Billing, [], pricier, draft.Version)));
        await s.Admin.PostAsync($"/api/v1/ops/agreements/{draft.Id}/activate", null);

        var after = await OpsScenario.Ok<InvoiceDto>(s.Admin.GetAsync($"/api/v1/ops/invoices/{invoice.Summary.Id}"));
        (after.Summary.GrandTotal, after.Lines.Single().Taxable, after.Lines.Single().AgreementRevision).ShouldBe((invoice.Summary.GrandTotal, invoice.Lines.Single().Taxable, 1));
        (await s.Admin.PostAsync($"/api/v1/ops/billing/calculate", System.Net.Http.Json.JsonContent.Create(new CalculateRequest([invoice.Lines.Single().LrId])))).StatusCode.ShouldBe(HttpStatusCode.OK); // returns a failure entry, never a new version
        (await OpsScenario.Ok<List<CalculationDto>>(s.Admin.GetAsync($"/api/v1/ops/lrs/{invoice.Lines.Single().LrId}/calculations"))).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelling_an_invoice_frees_its_deliveries_and_its_number_is_not_reused()
    {
        var (s, _, calc) = await ReviewedDeliveryAsync(factory);
        using var _s = s;
        var invoice = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null)));

        (await s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{invoice.Summary.Id}/cancel", new CancelInvoiceRequest(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var cancelled = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{invoice.Summary.Id}/cancel", new CancelInvoiceRequest("Wrong PO number")));
        cancelled.Summary.Status.ShouldBe(InvoiceStatus.Cancelled);
        (await OpsScenario.Ok<CalculationDto>(s.Admin.GetAsync($"/api/v1/ops/billing/calculations/{calc.Id}"))).Status.ShouldBe(CalculationStatus.Reviewed);

        var rebilled = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null)));

        Sequence(rebilled.Summary.Number).ShouldBe(Sequence(invoice.Summary.Number) + 1);
    }

    [Fact]
    public async Task A_customer_portal_rejection_leads_to_resubmission_and_the_whole_history_is_kept()
    {
        var (s, _, calc) = await ReviewedDeliveryAsync(factory);
        using var _s = s;
        var invoice = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null)));
        var id = invoice.Summary.Id;

        var sent = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/submit", new SubmitInvoiceRequest("PORTAL-001", "inv.pdf", "Uploaded by hand")));
        sent.Summary.Submission.ShouldBe(SubmissionStatus.Submitted);
        (await s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/submit", new SubmitInvoiceRequest(null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/cancel", new CancelInvoiceRequest("too late"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/reject-submission", new RejectSubmissionRequest(" "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var rejected = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/reject-submission", new RejectSubmissionRequest("PO number missing")));
        rejected.Summary.Submission.ShouldBe(SubmissionStatus.ResubmissionRequired);
        (await OpsScenario.Ok<PagedResult<InvoiceSummaryDto>>(s.Admin.GetAsync($"/api/v1/ops/invoices?submission=ResubmissionRequired&customerId={s.Customer.Id}"))).Items.Count.ShouldBe(1);

        (await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/submit", new SubmitInvoiceRequest("PORTAL-002", null, null)))).Summary.Submission.ShouldBe(SubmissionStatus.Submitted);
        var accepted = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/acknowledge", new AcknowledgeSubmissionRequest("ACK-9")));

        accepted.Summary.Submission.ShouldBe(SubmissionStatus.Acknowledged);
        accepted.Submissions.Select(x => (x.Attempt, x.Status, x.RejectionReason)).ShouldBe([(1, SubmissionStatus.Rejected, "PO number missing"), (2, SubmissionStatus.Acknowledged, null)]);
    }

    [Fact]
    public async Task Payments_reduce_the_balance_and_a_payment_that_does_not_fit_is_a_mismatch_that_changes_nothing()
    {
        var (s, _, calc) = await ReviewedDeliveryAsync(factory);
        using var _s = s;
        var invoice = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null)));
        var id = invoice.Summary.Id;
        var total = invoice.Summary.GrandTotal;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var part = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/payments", new PaymentRequest(1000m, today, "NEFT", "UTR1", null)));
        (part.Summary.PaymentState, part.Summary.Outstanding).ShouldBe((PaymentState.PartiallyPaid, total - 1000m));
        var receivables = await OpsScenario.Ok<List<ReceivableRowDto>>(s.Admin.GetAsync("/api/v1/receivables".Replace("/api/v1/", "/api/v1/ops/")));
        receivables.Single(r => r.CustomerId == s.Customer.Id).Total.ShouldBe(total - 1000m);

        var over = await s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/payments", new PaymentRequest(total, today, "NEFT", "UTR2", null));
        over.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await over.ProblemCodeAsync()).ShouldBe("ops.payment_mismatch");
        (await OpsScenario.Ok<InvoiceDto>(s.Admin.GetAsync($"/api/v1/ops/invoices/{id}"))).Summary.AmountPaid.ShouldBe(1000m);

        var paid = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/payments", new PaymentRequest(total - 1000m, today, "NEFT", "UTR3", "Balance")));
        (paid.Summary.PaymentState, paid.Summary.Outstanding).ShouldBe((PaymentState.Paid, 0m));
        paid.Payments.Count.ShouldBe(2);
        (await OpsScenario.Ok<List<ReceivableRowDto>>(s.Admin.GetAsync("/api/v1/ops/receivables"))).ShouldNotContain(r => r.CustomerId == s.Customer.Id);
        (await s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{id}/payments", new PaymentRequest(1m, today, null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_pod_reopened_before_invoicing_blocks_the_invoice_and_one_reopened_after_is_refused()
    {
        var (s, lrId, calc) = await ReviewedDeliveryAsync(factory);
        using var _s = s;

        (await OpsScenario.Ok<PodDto>(s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lrId}/pod/reopen", new PodDecisionRequest("Second look")))).Status.ShouldBe(PodStatus.UnderReview);
        var blocked = await s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null));
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.ProblemCodeAsync()).ShouldBe("ops.invoice_pod");

        await OpsScenario.Ok<PodDto>(s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lrId}/pod/accept", new PodDecisionRequest("Fine")));
        await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null)));
        var late = await s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lrId}/pod/reopen", new PodDecisionRequest("Too late"));
        (await late.ProblemCodeAsync()).ShouldBe("ops.pod_invoiced");
    }

    [Fact]
    public async Task The_times_of_an_invoiced_delivery_cannot_be_changed_but_an_uninvoiced_one_can_be_corrected_with_a_reason_and_recalculated()
    {
        using var s = await OpsScenario.CreateAsync(factory);
        var (lr, _) = await s.DeliveredAsync(TimeSpan.FromHours(8)); // 4 hours of detention
        await s.AcceptedPodAsync(lr.Lr.Lr.Id);
        var before = await s.CalculateAsync(lr.Lr.Lr.Id);
        before.DetentionAmount.ShouldBe(800m);

        var detail = await s.GetLrAsync(lr.Lr.Lr.Id);
        var unloaded = detail.Lr.UnloadedAt!.Value;
        (await s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/destination-events/correct", new CorrectDestinationRequest(EventType.UnloadingCompleted, unloaded.AddHours(-3), " "))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/destination-events/correct", new CorrectDestinationRequest(EventType.UnloadingCompleted, detail.Lr.ArrivedAt!.Value.AddHours(-1), "bad"))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var corrected = await OpsScenario.Ok<LrDetailDto>(s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/destination-events/correct", new CorrectDestinationRequest(EventType.UnloadingCompleted, unloaded.AddHours(-3), "Gate pass shows an earlier exit")));
        corrected.Events.ShouldContain(e => e.Type == EventType.Correction && e.CorrectsEventId != null);

        var after = await s.CalculateAsync(lr.Lr.Lr.Id);
        after.DetentionAmount.ShouldBe(200m); // 5 hours waited, 1 over

        var reviewed = await OpsScenario.Ok<CalculationDto>(s.Admin.PostJsonAsync($"/api/v1/ops/billing/calculations/{after.Id}/review", new ReviewCalculationRequest(null)));
        await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([reviewed.Id], null, null, null)));
        var locked = await s.Admin.PostJsonAsync($"/api/v1/ops/lrs/{lr.Lr.Lr.Id}/destination-events/correct", new CorrectDestinationRequest(EventType.UnloadingCompleted, unloaded, "undo"));
        locked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task With_an_approval_policy_the_invoice_waits_for_the_approver_and_is_issued_on_approval()
    {
        var s = await OpsScenario.CreateAsync(factory);
        using var _s = s;
        using var approver = await factory.UserWithPermissionsAsync(s.Admin, OpsPermissions.InvoiceManage);
        var policy = await s.Admin.PutJsonAsync("/api/v1/approvals/policies/customer_invoice", new SavePolicyRequest(true, [new PolicyStepDto("Finance approval", OpsPermissions.InvoiceManage, null)], null));
        policy.IsSuccessStatusCode.ShouldBeTrue(await policy.Content.ReadAsStringAsync());
        (await s.Admin.PutJsonAsync("/api/v1/ops/settings", new OpsSettingsDto(new ArrivalSetting(), new NumberingSetting(), new PodSetting(), new InvoiceSetting("INV", null, "27", true), new FleetSetting()))).EnsureSuccessStatusCode();
        try
        {
            var (_, _, calc) = await ReviewedDeliveryAsync(factory, existing: s);

            var pending = await OpsScenario.Ok<InvoiceDto>(s.Admin.PostJsonAsync("/api/v1/ops/invoices", new CreateInvoiceRequest([calc.Id], null, null, null)));

            pending.Summary.Status.ShouldBe(InvoiceStatus.PendingApproval);
            pending.ApprovalRequestId.ShouldNotBeNull();
            (await s.Admin.PostJsonAsync($"/api/v1/ops/invoices/{pending.Summary.Id}/submit", new SubmitInvoiceRequest(null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict); // not issued yet
            (await approver.PostJsonAsync($"/api/v1/approvals/requests/{pending.ApprovalRequestId}/approve", new DecisionRequest("Checked"))).StatusCode.ShouldBe(HttpStatusCode.OK);

            var issued = await OpsScenario.Ok<InvoiceDto>(s.Admin.GetAsync($"/api/v1/ops/invoices/{pending.Summary.Id}"));
            issued.Summary.Status.ShouldBe(InvoiceStatus.Issued);
        }
        finally
        {
            await s.Admin.PutJsonAsync("/api/v1/ops/settings", new OpsSettingsDto(new ArrivalSetting(), new NumberingSetting(), new PodSetting(), new InvoiceSetting("INV", null, "27", false), new FleetSetting()));
        }
    }

    [Fact]
    public async Task The_dashboard_counts_what_is_waiting_at_each_stage()
    {
        var (s, _, _) = await ReviewedDeliveryAsync(factory);
        using var _s = s;

        var d = await OpsScenario.Ok<DashboardDto>(s.Admin.GetAsync("/api/v1/ops/dashboard"));

        d.ReadyToInvoice.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(1);
        d.Outstanding.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(0);
    }
}
