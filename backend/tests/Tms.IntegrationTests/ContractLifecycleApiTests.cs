using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Approvals.Application;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Application.Lifecycle;
using Tms.Modules.Contracts.Domain;
using Tms.Modules.Platform.Application.Audit;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.ContractApiData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ContractLifecycleApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task Creating_a_contract_numbers_it_and_requires_an_active_transporter()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);

        var contract = await CreateAsync(admin, NewContract(transporter.Id));

        contract.Summary.Number.ShouldStartWith("CN-");
        contract.Summary.Status.ShouldBe(ContractStatus.Draft);
        contract.Summary.TransporterName.ShouldBe(transporter.LegalName);
        contract.MissingForSubmission.ShouldBe(["At least one rate"]);

        var draftOnly = await TransporterTestData.CreateAsync(admin); // never onboarded
        var rejected = await admin.PostJsonAsync("/api/v1/contracts", NewContract(draftOnly.Id));
        (await rejected.ProblemCodeAsync()).ShouldBe("contracts.transporter_inactive");
        var unknown = await admin.PostJsonAsync("/api/v1/contracts", NewContract(Guid.NewGuid()));
        (await unknown.ProblemCodeAsync()).ShouldBe("contracts.transporter_unknown");
    }

    [Fact]
    public async Task Header_problems_are_reported_by_field()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);

        var response = await admin.PostJsonAsync("/api/v1/contracts", NewContract(transporter.Id, from: Today, to: Today.AddDays(-1)));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("\"effectiveTo\"");
    }

    [Fact]
    public async Task Rates_round_trip_with_their_pricing_shape_and_are_validated()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var draft = await CreateAsync(admin, NewContract(transporter.Id));

        var saved = await WithRatesAsync(admin, draft,
            Flat(City("Maharashtra", "Pune"), City("Gujarat", "Surat"), 52_000m, truck.Id),
            new RateInputDto(Anywhere, Anywhere, false, truck.Id, 0, 250, new PerKmPricing(40m, 100m, 6_000m)));
        saved.Summary.RateCount.ShouldBe(2);

        var rates = await (await admin.GetAsync($"/api/v1/contracts/{draft.Summary.Id}/rates")).ReadAsync<List<RateCardDto>>();
        rates.Select(r => r.Pricing.GetType()).ShouldBe([typeof(FlatTripPricing), typeof(PerKmPricing)], ignoreOrder: true);
        rates.ShouldAllBe(r => r.VehicleTypeName == truck.Name);
        rates.Single(r => r.Pricing is FlatTripPricing).Lane.ShouldBe("PUNE, MAHARASHTRA → SURAT, GUJARAT");

        async Task<string> Rejection(RateInputDto rate)
        {
            var response = await admin.PutJsonAsync($"/api/v1/contracts/{draft.Summary.Id}/rates", new SaveRatesRequest([rate], saved.Version));
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
            return await response.Content.ReadAsStringAsync();
        }

        (await Rejection(new RateInputDto(Anywhere, Anywhere, false, null, null, null, new FlatTripPricing(10m)))).ShouldContain("choose a vehicle type");
        (await Rejection(Slabbed(State("Goa"), State("Goa"), (0, 100, 5)))).ShouldContain("does not suit a Ftl contract");
        (await Rejection(Flat(Zone("NOPE"), State("Goa"), 10m, truck.Id))).ShouldContain("Zone NOPE does not exist");
        (await Rejection(Flat(Zone("NOPE"), State("Goa"), 10m, Guid.NewGuid()))).ShouldContain("vehicle type that does not exist");
        (await Rejection(Flat(new PlaceDto(PlaceKind.City, "Goa", null, null), State("Goa"), 10m, truck.Id))).ShouldContain("needs a state and a city");
    }

    [Fact]
    public async Task Concurrent_edits_to_the_rates_are_detected()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var draft = await CreateAsync(admin, NewContract(transporter.Id));
        await WithRatesAsync(admin, draft, Flat(State("Goa"), State("Goa"), 1_000m, truck.Id));

        var stale = await admin.PutJsonAsync($"/api/v1/contracts/{draft.Summary.Id}/rates",
            new SaveRatesRequest([Flat(State("Goa"), State("Goa"), 2_000m, truck.Id)], draft.Version));

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await stale.ProblemCodeAsync()).ShouldBe("concurrency.conflict");
    }

    [Fact]
    public async Task A_contract_cannot_be_submitted_without_rates_and_becomes_immutable_once_approved()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        await NoApprovalNeededAsync(admin);
        var draft = await CreateAsync(admin, NewContract(transporter.Id));

        var empty = await admin.PostAsync($"/api/v1/contracts/{draft.Summary.Id}/submit", null);
        empty.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync()).ShouldContain("At least one rate");

        var withRates = await WithRatesAsync(admin, draft, Flat(State("Goa"), State("Goa"), 1_000m, truck.Id));
        var active = await SubmitAsync(admin, withRates.Summary.Id);
        active.Summary.Status.ShouldBe(ContractStatus.Active);

        var edit = await admin.PutJsonAsync($"/api/v1/contracts/{active.Summary.Id}/rates",
            new SaveRatesRequest([Flat(State("Goa"), State("Goa"), 9m, truck.Id)], active.Version));
        edit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await edit.ProblemCodeAsync()).ShouldBe("contracts.immutable");
    }

    [Fact]
    public async Task Approval_runs_through_the_engine_and_the_requester_cannot_approve_their_own_contract()
    {
        using var admin = await factory.AdminAsync();
        using var approver = await factory.UserWithPermissionsAsync(admin, ContractPermissions.Approve);
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        await OneApproverAsync(admin);
        var draft = await CreateAsync(admin, NewContract(transporter.Id, spend: 12_000_000m));
        var withRates = await WithRatesAsync(admin, draft, Flat(State("Goa"), State("Goa"), 1_000m, truck.Id));

        var pending = await SubmitAsync(admin, withRates.Summary.Id);

        pending.Summary.Status.ShouldBe(ContractStatus.PendingApproval);
        pending.ApprovalRequestId.ShouldNotBeNull();
        var edit = await admin.PutJsonAsync($"/api/v1/contracts/{pending.Summary.Id}/rates", new SaveRatesRequest([], pending.Version));
        (await edit.ProblemCodeAsync()).ShouldBe("contracts.locked");

        var request = await (await admin.GetAsync($"/api/v1/approvals/requests/{pending.ApprovalRequestId}")).ReadAsync<RequestDto>();
        request.Amount.ShouldBe(12_000_000m, "the matrix sees the estimated annual spend");
        request.DocumentTypeName.ShouldBe("Freight contract");

        (await admin.PostJsonAsync($"/api/v1/approvals/requests/{pending.ApprovalRequestId}/approve", new DecisionRequest(null))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await approver.PostJsonAsync($"/api/v1/approvals/requests/{pending.ApprovalRequestId}/approve", new DecisionRequest("Rates benchmarked")))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetAsync(admin, pending.Summary.Id)).Summary.Status.ShouldBe(ContractStatus.Active);
    }

    [Fact]
    public async Task A_rejected_contract_can_be_corrected_and_resubmitted()
    {
        using var admin = await factory.AdminAsync();
        using var approver = await factory.UserWithPermissionsAsync(admin, ContractPermissions.Approve);
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        await OneApproverAsync(admin);
        var draft = await WithRatesAsync(admin, await CreateAsync(admin, NewContract(transporter.Id)), Flat(State("Goa"), State("Goa"), 5_000m, truck.Id));
        var pending = await SubmitAsync(admin, draft.Summary.Id);

        await approver.PostJsonAsync($"/api/v1/approvals/requests/{pending.ApprovalRequestId}/reject", new DecisionRequest("Rate is 15% above benchmark"));

        var rejected = await GetAsync(admin, pending.Summary.Id);
        rejected.Summary.Status.ShouldBe(ContractStatus.Rejected);
        var fixedRates = await WithRatesAsync(admin, rejected, Flat(State("Goa"), State("Goa"), 4_300m, truck.Id));
        var again = await SubmitAsync(admin, fixedRates.Summary.Id);
        again.ApprovalRequestId.ShouldNotBe(pending.ApprovalRequestId);
        (await approver.PostJsonAsync($"/api/v1/approvals/requests/{again.ApprovalRequestId}/approve", new DecisionRequest(null))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(admin, again.Summary.Id)).Summary.Status.ShouldBe(ContractStatus.Active);
    }

    [Fact]
    public async Task A_revision_replaces_the_contract_from_its_start_date_and_history_stays_priceable()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var original = await ActiveContractAsync(admin, transporter.Id, ContractType.Ftl, null,
            Flat(City("Maharashtra", "Pune"), City("Gujarat", "Surat"), 50_000m, truck.Id));
        var switchDay = Today.AddDays(10);

        var revisionResponse = await admin.PostJsonAsync($"/api/v1/contracts/{original.Summary.Id}/revise", new ReviseRequest(switchDay, switchDay.AddYears(1)));
        revisionResponse.StatusCode.ShouldBe(HttpStatusCode.Created, await revisionResponse.Content.ReadAsStringAsync());
        var revision = await revisionResponse.ReadAsync<ContractDto>();
        revision.Summary.Revision.ShouldBe(2);
        revision.Summary.Reference.ShouldBe($"{original.Summary.Number} · R2");
        revision.Summary.RateCount.ShouldBe(1);
        (await admin.PostJsonAsync($"/api/v1/contracts/{original.Summary.Id}/revise", new ReviseRequest(switchDay, switchDay.AddYears(1))))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var revised = await WithRatesAsync(admin, revision, Flat(City("Maharashtra", "Pune"), City("Gujarat", "Surat"), 46_000m, truck.Id));
        (await SubmitAsync(admin, revised.Summary.Id)).Summary.Status.ShouldBe(ContractStatus.Active);

        var old = await GetAsync(admin, original.Summary.Id);
        old.Summary.Status.ShouldBe(ContractStatus.Superseded);
        old.Summary.EffectiveTo.ShouldBe(switchDay.AddDays(-1));

        decimal Price(QuoteResultDto result, string contract) => result.Quotes.Single(q => q.ContractReference.StartsWith(contract, StringComparison.Ordinal)).Total;
        var before = await QuoteAsync(admin, Quote("Maharashtra", "Pune", "Gujarat", "Surat", truck.Id, date: switchDay.AddDays(-1), contractId: original.Summary.Id));
        var after = await QuoteAsync(admin, Quote("Maharashtra", "Pune", "Gujarat", "Surat", truck.Id, date: switchDay, contractId: revised.Summary.Id));
        Price(before, original.Summary.Number).ShouldBe(50_000m);
        Price(after, original.Summary.Number).ShouldBe(46_000m);

        (await QuoteAsync(admin, Quote("Maharashtra", "Pune", "Gujarat", "Surat", truck.Id, date: switchDay, contractId: original.Summary.Id)))
            .Quotes.ShouldBeEmpty("the superseded contract does not govern shipments from the switch date");
    }

    [Fact]
    public async Task Termination_needs_a_reason_and_stops_pricing_from_the_next_day()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var contract = await ActiveContractAsync(admin, transporter.Id, ContractType.Ftl, null, Flat(State("Goa"), State("Goa"), 1_000m, truck.Id));

        (await admin.PostJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/terminate", new TerminateRequest(""))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var terminated = await (await admin.PostJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/terminate", new TerminateRequest("Repeated service failures"))).ReadAsync<ContractDto>();

        terminated.Summary.Status.ShouldBe(ContractStatus.Terminated);
        terminated.TerminationReason.ShouldBe("Repeated service failures");
        (await QuoteAsync(admin, Quote("Goa", "Panaji", "Goa", "Margao", truck.Id, contractId: contract.Summary.Id, date: Today.AddDays(1)))).Quotes.ShouldBeEmpty();
        (await QuoteAsync(admin, Quote("Goa", "Panaji", "Goa", "Margao", truck.Id, contractId: contract.Summary.Id, date: Today.AddDays(-2)))).Quotes.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task The_nightly_job_sends_one_reminder_per_threshold_then_marks_the_contract_expired()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var request = NewContract(transporter.Id, from: Today.AddDays(-300), to: Today.AddDays(40));
        var contract = await ActiveContractAsync(admin, transporter.Id, ContractType.Ftl, request, Flat(State("Goa"), State("Goa"), 1_000m, truck.Id));
        var id = contract.Summary.Id;
        var job = factory.Services.GetServices<IHostedService>().OfType<ContractLifecycleService>().Single();
        var mailbox = factory.Services.GetRequiredService<CapturingEmailSender>();
        int Reminders() => mailbox.Sent.Count(m => m.Subject.Contains(contract.Summary.Reference, StringComparison.Ordinal));

        var expiring = await (await admin.GetAsync("/api/v1/contracts/expiring?withinDays=60&pageSize=200")).ReadAsync<PagedResult<ContractSummaryDto>>();
        expiring.Items.ShouldContain(c => c.Id == id);

        await job.RunOnceAsync(Today.AddDays(5), CancellationToken.None);          // 35 days left → 60-day threshold crossed
        Reminders().ShouldBe(1);
        await job.RunOnceAsync(Today.AddDays(6), CancellationToken.None);          // same threshold: nothing new
        Reminders().ShouldBe(1);
        await job.RunOnceAsync(Today.AddDays(30), CancellationToken.None);         // 10 days left → 15-day threshold
        Reminders().ShouldBe(2);
        mailbox.Sent.Last(m => m.Subject.Contains(contract.Summary.Reference, StringComparison.Ordinal)).To.ShouldBe(TmsApiFactory.DemoAdminEmail);
        mailbox.Sent.Last(m => m.Subject.Contains(contract.Summary.Reference, StringComparison.Ordinal)).TextBody.ShouldContain($"/contracts/{id}");

        (await GetAsync(admin, id)).Summary.Status.ShouldBe(ContractStatus.Active);
        await job.RunOnceAsync(Today.AddDays(41), CancellationToken.None);        // past the end date
        (await GetAsync(admin, id)).Summary.Status.ShouldBe(ContractStatus.Expired);
        Reminders().ShouldBe(2, "no reminder for something already expired");
    }

    [Fact]
    public async Task Changes_land_in_the_audit_trail_without_one_row_per_rate()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var draft = await CreateAsync(admin, NewContract(transporter.Id));
        await WithRatesAsync(admin, draft, Flat(State("Goa"), State("Goa"), 1_000m, truck.Id), Flat(State("Kerala"), State("Kerala"), 1_200m, truck.Id));

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityId={draft.Summary.Id}")).ReadAsync<PagedResult<AuditLogDto>>();
        logs.Items.Select(l => l.Action).ShouldBe(["Updated", "Created"]);
        logs.Items[0].Changes!.Value.GetProperty("RateCount").GetProperty("new").GetInt32().ShouldBe(2);

        var rateRows = await (await admin.GetAsync("/api/v1/audit-logs?entityType=RateCard&pageSize=200")).ReadAsync<PagedResult<AuditLogDto>>();
        rateRows.Items.ShouldBeEmpty("rate rows are summarised on the contract, not audited one by one");
    }
}
