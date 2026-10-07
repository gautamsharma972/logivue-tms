using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Contracts.Application;
using Tms.Modules.Contracts.Domain;
using Tms.SharedKernel.Contracts;
using static Tms.IntegrationTests.Infrastructure.ContractApiData;
using static Tms.IntegrationTests.Infrastructure.FreightApiData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class FreightContractApiTests(TmsApiFactory factory)
{
    [Fact]
    public async Task A_contract_carries_its_currency_services_contact_and_renewal_terms_and_the_spec_routes_reach_the_same_contracts()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var request = NewContract(transporter.Id) with { Extras = new ContractExtras("inr", "Retail BU", "Asha 98xxxxxx01", 45, true, [ContractType.Ptl, ContractType.Dedicated]) };

        var created = await (await admin.PostJsonAsync("/api/v1/freight-contracts", request)).ReadAsync<ContractDto>();

        created.Extras!.Currency.ShouldBe("INR");
        created.Extras.BusinessUnit.ShouldBe("Retail BU");
        created.Extras.RenewalNoticeDays.ShouldBe(45);
        created.Extras.AutoRenewal.ShouldBeTrue();
        created.Summary.Services.ShouldBe([ContractType.Ftl, ContractType.Ptl, ContractType.Dedicated], ignoreOrder: true);
        created.CalculationVersion.ShouldBe("1.0");
        (await (await admin.GetAsync($"/api/v1/freight-contracts/{created.Summary.Id}")).ReadAsync<ContractDto>()).Summary.Number.ShouldBe(created.Summary.Number);
        (await admin.PostJsonAsync("/api/v1/freight-contracts", NewContract(transporter.Id) with { Extras = new ContractExtras("RUPEES") })).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_contract_can_be_suspended_resumed_and_a_draft_cancelled()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var active = await BuildActiveAsync(admin, transporter.Id, rates: [Flat(State(state), State(state), 40_000m, truck.Id)]);
        var id = active.Summary.Id;

        (await admin.PostJsonAsync($"/api/v1/contracts/{id}/suspend", new ContractReasonRequest(""))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var suspended = await (await admin.PostJsonAsync($"/api/v1/contracts/{id}/suspend", new ContractReasonRequest("Dispute over invoices"))).ReadAsync<ContractDto>();
        suspended.Summary.Status.ShouldBe(ContractStatus.Suspended);

        // while suspended, its rates do not rate a shipment
        var during = await CalculateAsync(admin, Rating(state, "A", state, "B", vehicle: truck.Id, km: 100m, transporter: transporter.Id));
        during.Qualified.ShouldBeFalse();
        during.Exclusions.ShouldContain(e => e.ReasonCode == "CONTRACT_SUSPENDED");

        (await (await admin.PostAsync($"/api/v1/contracts/{id}/resume", null)).ReadAsync<ContractDto>()).Summary.Status.ShouldBe(ContractStatus.Active);

        var draft = await CreateAsync(admin, NewContract(transporter.Id));
        (await admin.PostJsonAsync($"/api/v1/contracts/{draft.Summary.Id}/cancel", new ContractReasonRequest("Not needed"))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(admin, draft.Summary.Id)).Summary.Status.ShouldBe(ContractStatus.Cancelled);
        (await admin.PostJsonAsync($"/api/v1/contracts/{id}/cancel", new ContractReasonRequest("No"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_renewal_is_a_draft_with_an_uplift_and_numbered_rate_versions_and_there_can_be_only_one_at_a_time()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var active = await BuildActiveAsync(admin, transporter.Id, NewContract(transporter.Id, from: Today.AddDays(-300), to: Today.AddDays(30)), rates: [Flat(State(state), State(state), 40_000m, truck.Id)]);

        var response = await admin.PostJsonAsync($"/api/v1/contracts/{active.Summary.Id}/renew", new RenewRequest(null, null, 5m));
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var renewal = await response.ReadAsync<ContractDto>();

        renewal.Summary.Status.ShouldBe(ContractStatus.Draft);
        renewal.Summary.RevisionKind.ShouldBe(RevisionKind.Renewal);
        renewal.Summary.EffectiveFrom.ShouldBe(active.Summary.EffectiveTo.AddDays(1));
        var rates = await (await admin.GetAsync($"/api/v1/contracts/{renewal.Summary.Id}/rates")).ReadAsync<List<RateCardDto>>();
        ((FlatTripPricing)rates.Single().Pricing).AmountPerTrip.ShouldBe(42_000m);
        rates.Single().Version.ShouldBe(2);
        (await admin.PostJsonAsync($"/api/v1/contracts/{active.Summary.Id}/renew", new RenewRequest(null, null, null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var listed = await (await admin.GetAsync($"/api/v1/contracts?search={active.Summary.Number}")).ReadAsync<Tms.SharedKernel.Paging.PagedResult<ContractSummaryDto>>();
        listed.Items.Single(c => c.Id == active.Summary.Id).RenewalState.ShouldBe("RenewalInProgress");
    }

    [Fact]
    public async Task A_contract_is_approved_or_rejected_from_its_own_routes_by_someone_the_policy_allows()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        await OneApproverAsync(admin);
        var first = await WithRatesAsync(admin, await CreateAsync(admin, NewContract(transporter.Id)), Flat(State(state), State(state), 40_000m, truck.Id));
        var second = await WithRatesAsync(admin, await CreateAsync(admin, NewContract(transporter.Id)), Flat(State(UniqueState()), State(state), 41_000m, truck.Id));
        (await SubmitAsync(admin, first.Summary.Id)).Summary.Status.ShouldBe(ContractStatus.PendingApproval);
        (await SubmitAsync(admin, second.Summary.Id)).Summary.Status.ShouldBe(ContractStatus.PendingApproval);

        using var approver = await factory.UserWithPermissionsAsync(admin, ContractPermissions.Approve);

        (await admin.PostJsonAsync($"/api/v1/freight-contracts/{first.Summary.Id}/approve", new DecisionBody("Own contract"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden); // the requester cannot approve their own
        (await approver.PostJsonAsync($"/api/v1/freight-contracts/{second.Summary.Id}/reject", new DecisionBody(null))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var rejected = await approver.PostJsonAsync($"/api/v1/freight-contracts/{second.Summary.Id}/reject", new DecisionBody("Rates too high"));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK, await rejected.Content.ReadAsStringAsync());
        (await rejected.ReadAsync<ContractDto>()).Summary.Status.ShouldBe(ContractStatus.Rejected);
        var approved = await approver.PostJsonAsync($"/api/v1/contracts/{first.Summary.Id}/approve", new DecisionBody("Agreed"));
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        (await approved.ReadAsync<ContractDto>()).Summary.Status.ShouldBe(ContractStatus.Active);
        (await approver.PostJsonAsync($"/api/v1/contracts/{first.Summary.Id}/approve", new DecisionBody(null))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Dph_rules_charges_capacity_and_service_levels_are_edited_on_a_draft_and_locked_once_approved()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var truck = await VehicleTypeAsync(admin);
        var state = UniqueState();
        var region = UniqueRegion();
        var contract = await CreateAsync(admin, NewContract(transporter.Id));

        var withDph = await (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/dph-rules", new SaveDphRulesRequest([Dph(region)], contract.Version))).ReadAsync<ContractDto>();
        withDph.DphRuleCount.ShouldBe(1);
        (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/dph-rules", new SaveDphRulesRequest([Dph(region), Dph(region, "OTHER")], withDph.Version))).StatusCode.ShouldBe(HttpStatusCode.BadRequest); // two defaults at once

        (await admin.GetAsync("/api/v1/accessorials")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var detention = new AccessorialSpec("DETENTION", "Detention", AccessorialCalc.Tiered, "HOUR", Tiers: [new AccessorialTier(0, 2, 0), new AccessorialTier(2, 5, 500), new AccessorialTier(5, null, 750)]);
        var withCharges = await (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/accessorials", new SaveAccessorialsRequest([detention], withDph.Version))).ReadAsync<ContractDto>();
        withCharges.AccessorialCount.ShouldBe(1);
        (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/accessorials", new SaveAccessorialsRequest([detention with { Code = "NOT_IN_CATALOGUE" }], withCharges.Version))).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var withCapacity = await (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/capacity",
            new SaveCapacityRequest([new CapacitySpec(truck.Id, 6, 18_000m, 250, null, 60m, null, null)], withCharges.Version))).ReadAsync<ContractDto>();
        withCapacity.CapacityCount.ShouldBe(1);
        var withSla = await (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/sla",
            new SaveSlaRequest([new SlaSpec(ContractType.Ftl, Place.OfState(state), Place.OfState(state), null, 36 * 60, null, 6 * 60, [DayOfWeek.Monday, DayOfWeek.Tuesday], new TimeOnly(18, 0))], withCapacity.Version))).ReadAsync<ContractDto>();
        var slaRead = await admin.GetAsync($"/api/v1/contracts/{contract.Summary.Id}/sla");
        withSla.SlaCount.ShouldBe(1, await slaRead.Content.ReadAsStringAsync());
        (await (await admin.GetAsync($"/api/v1/contracts/{contract.Summary.Id}/sla")).ReadAsync<List<SlaSpec>>()).Single().TransitSlaMinutes.ShouldBe(2160);

        var withRates = await WithRatesAsync(admin, withSla, Flat(State(state), State(state), 40_000m, truck.Id));
        var active = await SubmitAsync(admin, withRates.Summary.Id);
        (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/dph-rules", new SaveDphRulesRequest([Dph(region)], active.Version))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.PutJsonAsync($"/api/v1/contracts/{contract.Summary.Id}/capacity", new SaveCapacityRequest([], active.Version))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Contract_documents_get_versions_are_verified_and_a_verified_one_is_never_deleted()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await ActiveTransporterAsync(admin);
        var contract = await CreateAsync(admin, NewContract(transporter.Id));

        async Task<ContractDocumentDto> Upload(string number, string kind = "RateAnnexure")
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(kind), "kind" }, { new StringContent($"Annexure {number}"), "title" }, { new StringContent(number), "number" },
                { new StringContent(Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), "issueDate" }, { new StringContent(Today.AddYears(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)), "expiryDate" },
            };
            var file = new ByteArrayContent(TransporterTestData.Pdf);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            form.Add(file, "file", "annexure.pdf");
            var response = await admin.PostAsync($"/api/v1/freight-contracts/{contract.Summary.Id}/documents", form);
            response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            return await response.ReadAsync<ContractDocumentDto>();
        }

        var v1 = await Upload("ANX-1");
        var v2 = await Upload("ANX-1");
        var other = await Upload("ANX-2");

        v1.DocumentVersion.ShouldBe(1);
        v2.DocumentVersion.ShouldBe(2);
        other.DocumentVersion.ShouldBe(1);
        v1.Kind.ShouldBe(ContractDocumentKind.RateAnnexure);
        v1.ExpiryDate.ShouldBe(Today.AddYears(1));
        var listed = await (await admin.GetAsync($"/api/v1/freight-contracts/{contract.Summary.Id}/documents")).ReadAsync<List<ContractDocumentDto>>();
        listed.Count(d => d.Number == "ANX-1").ShouldBe(2, "the earlier version is kept, not replaced");

        var verified = await (await admin.PostAsync($"/api/v1/freight-contracts/{contract.Summary.Id}/documents/{v2.Id}/verify", null)).ReadAsync<ContractDocumentDto>();
        verified.Status.ShouldBe(DocumentStatus.Verified);
        verified.VerifiedAt.ShouldNotBeNull();
        (await admin.PostAsync($"/api/v1/freight-contracts/{contract.Summary.Id}/documents/{v2.Id}/verify", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/v1/contract-documents/{v2.Id}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/v1/contract-documents/{other.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_standard_charge_catalogue_is_offered_and_a_tenant_can_add_its_own()
    {
        using var admin = await factory.AdminAsync();

        var types = await (await admin.GetAsync("/api/v1/accessorials")).ReadAsync<List<AccessorialTypeDto>>();
        types.Select(t => t.Code).ShouldContain("DETENTION");
        types.Select(t => t.Code).ShouldContain("TOLL");

        var code = $"CUSTOM_{Guid.NewGuid():N}"[..14].ToUpperInvariant();
        var created = await admin.PostJsonAsync("/api/v1/accessorials", new SaveAccessorialTypeRequest(code, "Custom handling", "Our own", AccessorialCalc.PerUnit, "box"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        (await admin.PostJsonAsync("/api/v1/accessorials", new SaveAccessorialTypeRequest(code, "Again", null, AccessorialCalc.PerUnit, "box"))).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var dto = await created.ReadAsync<AccessorialTypeDto>();
        (await admin.PutJsonAsync($"/api/v1/accessorials/{dto.Id}", new SaveAccessorialTypeRequest(code, "Renamed", null, AccessorialCalc.Fixed, "trip", false))).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await (await admin.GetAsync("/api/v1/accessorials")).ReadAsync<List<AccessorialTypeDto>>()).Single(t => t.Code == code).IsActive.ShouldBeFalse();
    }
}
