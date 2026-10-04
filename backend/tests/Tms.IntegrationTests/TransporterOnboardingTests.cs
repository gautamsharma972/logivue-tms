using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Approvals.Application;
using Tms.Modules.Platform.Application.Audit;
using Tms.Modules.Transporters.Application;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Paging;
using static Tms.IntegrationTests.Infrastructure.TransporterTestData;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TransporterOnboardingTests(TmsApiFactory factory)
{
    [Fact]
    public async Task Creating_a_transporter_validates_every_field_at_once()
    {
        using var admin = await factory.AdminAsync();
        var bad = NewRequest() with { Pan = "BAD", Gstin = "27AAPFU0939F1ZW", Phone = "123", Pincode = "12", Email = "nope", LegalName = " " };

        var response = await admin.PostJsonAsync("/api/v1/transporters", bad);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        foreach (var field in new[] { "legalName", "pan", "gstin", "phone", "pincode", "email" })
        {
            body.ShouldContain($"\"{field}\"");
        }
    }

    [Fact]
    public async Task New_transporters_get_sequential_codes_and_start_as_drafts()
    {
        using var admin = await factory.AdminAsync();

        var first = await CreateAsync(admin);
        var second = await CreateAsync(admin);

        first.Status.ShouldBe(TransporterStatus.Draft);
        first.Code.ShouldStartWith("TR-");
        int.Parse(second.Code[3..], System.Globalization.CultureInfo.InvariantCulture)
            .ShouldBe(int.Parse(first.Code[3..], System.Globalization.CultureInfo.InvariantCulture) + 1);
        first.Phone.ShouldBe("9876543210");
    }

    [Fact]
    public async Task The_same_PAN_or_GSTIN_cannot_be_registered_twice_but_another_tenant_may()
    {
        using var admin = await factory.AdminAsync();
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        var original = NewRequest();
        await CreateAsync(admin, original);

        var samePan = await admin.PostJsonAsync("/api/v1/transporters", NewRequest(pan: original.Pan));
        samePan.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await samePan.ProblemCodeAsync()).ShouldBe("transporters.pan_exists");

        // A different PAN but the same GSTIN as an existing one (which also implies the PAN clash) is caught as well.
        var sameGstin = await admin.PostJsonAsync("/api/v1/transporters", original with { LegalName = "Other Name Ltd" });
        sameGstin.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        (await acme.PostJsonAsync("/api/v1/transporters", original)).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_draft_lists_what_is_missing_and_cannot_be_submitted_until_complete()
    {
        using var admin = await factory.AdminAsync();
        await NeedsOneApproverAsync(admin);
        var draft = await CreateAsync(admin);

        draft.MissingForSubmission.ShouldBe(["Bank details", "PAN card", "Cancelled cheque", "GST registration certificate"]);

        var response = await admin.PostAsync($"/api/v1/transporters/{draft.Id}/submit", null);
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ProblemCodeAsync()).ShouldBe("transporters.onboarding_incomplete");
        (await response.Content.ReadAsStringAsync()).ShouldContain("Cancelled cheque");
        (await GetAsync(admin, draft.Id)).Status.ShouldBe(TransporterStatus.Draft);
    }

    [Fact]
    public async Task Onboarding_runs_through_the_approval_engine_and_activates_the_transporter()
    {
        using var admin = await factory.AdminAsync();
        await NeedsOneApproverAsync(admin);
        using var approver = await factory.UserWithPermissionsAsync(admin, TransporterPermissions.Approve);
        var ready = await CreateReadyAsync(admin);
        ready.MissingForSubmission.ShouldBeEmpty();

        var submitted = await admin.PostAsync($"/api/v1/transporters/{ready.Id}/submit", null);
        submitted.StatusCode.ShouldBe(HttpStatusCode.OK, await submitted.Content.ReadAsStringAsync());
        var pending = await submitted.ReadAsync<TransporterDto>();
        pending.Status.ShouldBe(TransporterStatus.PendingApproval);
        pending.ApprovalRequestId.ShouldNotBeNull();

        // Locked while pending.
        var edit = await admin.PutJsonAsync($"/api/v1/transporters/{ready.Id}", NewRequest(pan: ready.Pan) with { Version = pending.Version });
        edit.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await edit.ProblemCodeAsync()).ShouldBe("transporters.locked");

        // The requester cannot approve their own onboarding; an approver can.
        (await ApproveAsync(admin, pending.ApprovalRequestId!.Value)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApproveAsync(approver, pending.ApprovalRequestId!.Value, "Documents verified")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var active = await GetAsync(admin, ready.Id);
        active.Status.ShouldBe(TransporterStatus.Active);
        active.ActivatedAt.ShouldNotBeNull();

        // Identity is frozen after approval; contact details are not.
        var rename = await admin.PutJsonAsync($"/api/v1/transporters/{ready.Id}", NewRequest(pan: ready.Pan) with
        {
            LegalName = "Totally Different Ltd", Version = active.Version,
        });
        (await rename.ProblemCodeAsync()).ShouldBe("transporters.identity_frozen");

        var contact = await admin.PutJsonAsync($"/api/v1/transporters/{ready.Id}", new SaveTransporterRequest(
            active.LegalName, active.TradeName, active.Pan, active.Gstin, "New Contact", "9123456780", active.Email,
            "Plot 4, MIDC", null, "Pune", "Maharashtra", "411019", ["Ftl"], active.Version));
        contact.StatusCode.ShouldBe(HttpStatusCode.OK, await contact.Content.ReadAsStringAsync());
        (await contact.ReadAsync<TransporterDto>()).ContactPerson.ShouldBe("New Contact");
    }

    [Fact]
    public async Task A_rejected_onboarding_can_be_corrected_and_resubmitted()
    {
        using var admin = await factory.AdminAsync();
        await NeedsOneApproverAsync(admin);
        using var approver = await factory.UserWithPermissionsAsync(admin, TransporterPermissions.Approve);
        var ready = await CreateReadyAsync(admin);
        var first = await (await admin.PostAsync($"/api/v1/transporters/{ready.Id}/submit", null)).ReadAsync<TransporterDto>();

        var rejected = await approver.PostJsonAsync($"/api/v1/approvals/requests/{first.ApprovalRequestId}/reject", new DecisionRequest("Cheque is unreadable"));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterReject = await GetAsync(admin, ready.Id);
        afterReject.Status.ShouldBe(TransporterStatus.Rejected);

        await UploadOkAsync(admin, ready.Id, OwnerKind.Transporter, ready.Id, DocumentKind.CancelledCheque); // replace the bad scan
        var second = await (await admin.PostAsync($"/api/v1/transporters/{ready.Id}/submit", null)).ReadAsync<TransporterDto>();
        second.Status.ShouldBe(TransporterStatus.PendingApproval);
        second.ApprovalRequestId.ShouldNotBe(first.ApprovalRequestId);

        (await ApproveAsync(approver, second.ApprovalRequestId!.Value)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(admin, ready.Id)).Status.ShouldBe(TransporterStatus.Active);
    }

    [Fact]
    public async Task Cancelling_the_approval_returns_the_transporter_to_draft()
    {
        using var admin = await factory.AdminAsync();
        await NeedsOneApproverAsync(admin);
        var ready = await CreateReadyAsync(admin);
        var pending = await (await admin.PostAsync($"/api/v1/transporters/{ready.Id}/submit", null)).ReadAsync<TransporterDto>();

        (await admin.PostAsync($"/api/v1/approvals/requests/{pending.ApprovalRequestId}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (await GetAsync(admin, ready.Id)).Status.ShouldBe(TransporterStatus.Draft);
    }

    [Fact]
    public async Task When_no_approval_step_applies_the_transporter_is_activated_immediately()
    {
        using var admin = await factory.AdminAsync();
        await SetOnboardingPolicyAsync(admin, new PolicyStepDto("Only for huge vendors", TransporterPermissions.Approve, 1_000_000_000m));
        var ready = await CreateReadyAsync(admin);

        var response = await admin.PostAsync($"/api/v1/transporters/{ready.Id}/submit", null);

        (await response.ReadAsync<TransporterDto>()).Status.ShouldBe(TransporterStatus.Active);
    }

    [Fact]
    public async Task Without_an_active_policy_submission_is_refused_and_the_draft_is_untouched()
    {
        using var admin = await factory.AdminAsync();
        await NeedsOneApproverAsync(admin);
        var current = (await (await admin.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<PolicyDto>>()).Single(p => p.DocumentType == "transporter_onboarding");
        await admin.PutJsonAsync("/api/v1/approvals/policies/transporter_onboarding", new SavePolicyRequest(false, current.Steps, current.Version));
        var ready = await CreateReadyAsync(admin);

        var response = await admin.PostAsync($"/api/v1/transporters/{ready.Id}/submit", null);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("approvals.no_policy");
        (await GetAsync(admin, ready.Id)).Status.ShouldBe(TransporterStatus.Draft);
    }

    [Fact]
    public async Task Active_transporters_can_be_suspended_with_a_reason_and_reactivated()
    {
        using var admin = await factory.AdminAsync();
        await SetOnboardingPolicyAsync(admin, new PolicyStepDto("Only for huge vendors", TransporterPermissions.Approve, 1_000_000_000m));
        var ready = await CreateReadyAsync(admin);
        await admin.PostAsync($"/api/v1/transporters/{ready.Id}/submit", null);

        var noReason = await admin.PostJsonAsync($"/api/v1/transporters/{ready.Id}/suspend", new SuspendRequest(""));
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var suspended = await (await admin.PostJsonAsync($"/api/v1/transporters/{ready.Id}/suspend", new SuspendRequest("Insurance lapsed"))).ReadAsync<TransporterDto>();
        suspended.Status.ShouldBe(TransporterStatus.Suspended);
        suspended.SuspensionReason.ShouldBe("Insurance lapsed");

        var back = await (await admin.PostAsync($"/api/v1/transporters/{ready.Id}/reactivate", null)).ReadAsync<TransporterDto>();
        back.Status.ShouldBe(TransporterStatus.Active);
        back.SuspensionReason.ShouldBeNull();
    }

    [Fact]
    public async Task Bank_details_are_masked_and_only_changeable_with_the_bank_permission()
    {
        using var admin = await factory.AdminAsync();
        using var manager = await factory.UserWithPermissionsAsync(admin, TransporterPermissions.Read, TransporterPermissions.Manage);
        var transporter = await CreateAsync(admin);

        var forbidden = await manager.PutJsonAsync($"/api/v1/transporters/{transporter.Id}/bank",
            new SaveBankRequest("X", "123456789012", "HDFC0001234", "HDFC", transporter.Version));
        forbidden.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await SetBankAsync(admin, transporter);
        var read = await GetAsync(manager, transporter.Id);
        read.Bank!.AccountNumberMasked.ShouldBe("••••••••9012");
        (await manager.GetStringAsync($"/api/v1/transporters/{transporter.Id}")).ShouldNotContain("123456789012");

        var badIfsc = await admin.PutJsonAsync($"/api/v1/transporters/{transporter.Id}/bank",
            new SaveBankRequest("X", "123456789012", "BAD", "HDFC", read.Version));
        (await badIfsc.Content.ReadAsStringAsync()).ShouldContain("\"ifsc\"");
    }

    [Fact]
    public async Task Staff_without_transporter_permissions_see_nothing_and_other_tenants_cannot_see_ours()
    {
        using var admin = await factory.AdminAsync();
        using var nobody = await factory.UserWithPermissionsAsync(admin);
        using var viewer = await factory.UserWithPermissionsAsync(admin, TransporterPermissions.Read);
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        var transporter = await CreateAsync(admin);

        (await nobody.GetAsync("/api/v1/transporters")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await nobody.GetAsync($"/api/v1/transporters/{transporter.Id}")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await viewer.GetAsync($"/api/v1/transporters/{transporter.Id}")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await viewer.PostJsonAsync("/api/v1/transporters", NewRequest())).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await acme.GetAsync($"/api/v1/transporters/{transporter.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var acmeList = await (await acme.GetAsync("/api/v1/transporters?pageSize=200")).ReadAsync<PagedResult<TransporterSummaryDto>>();
        acmeList.Items.ShouldNotContain(t => t.Id == transporter.Id);
    }

    [Fact]
    public async Task Listing_searches_by_name_code_and_PAN_and_filters_by_status()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin, NewRequest(name: "Zebra Carriers"));

        foreach (var term in new[] { "Zebra Carriers", transporter.Code, transporter.Pan[..7] })
        {
            var page = await (await admin.GetAsync($"/api/v1/transporters?search={Uri.EscapeDataString(term)}")).ReadAsync<PagedResult<TransporterSummaryDto>>();
            page.Items.ShouldContain(t => t.Id == transporter.Id);
        }

        var drafts = await (await admin.GetAsync($"/api/v1/transporters?status=Draft&search={transporter.Code}")).ReadAsync<PagedResult<TransporterSummaryDto>>();
        drafts.Items.ShouldHaveSingleItem().Id.ShouldBe(transporter.Id);
        var active = await (await admin.GetAsync($"/api/v1/transporters?status=Active&search={transporter.Code}")).ReadAsync<PagedResult<TransporterSummaryDto>>();
        active.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Changes_are_in_the_audit_trail()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        await SetBankAsync(admin, transporter);

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityType=Transporter&entityId={transporter.Id}")).ReadAsync<PagedResult<AuditLogDto>>();

        logs.Items.Select(l => l.Action).ShouldBe(["Updated", "Created"]);
        logs.Items[0].Changes!.Value.GetProperty("BankIfsc").GetProperty("new").GetString().ShouldBe("HDFC0001234");
    }

    [Fact]
    public async Task Bank_account_numbers_are_encrypted_in_the_database_and_masked_in_the_audit_trail()
    {
        using var admin = await factory.AdminAsync();
        var transporter = await CreateAsync(admin);
        await SetBankAsync(admin, transporter); // account number 123456789012

        string? stored;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tms.Modules.Transporters.Infrastructure.Persistence.TransportersDbContext>();
            stored = (await db.Database
                .SqlQuery<string>($"SELECT bank_account_number AS Value FROM transporters_transporters WHERE id = {transporter.Id}")
                .ToListAsync()).Single();
        }

        stored.ShouldStartWith("v1.1.");
        stored.ShouldNotContain("123456789012");
        (await GetAsync(admin, transporter.Id)).Bank!.AccountNumberMasked.ShouldBe("••••••••9012", "the application still reads it back");

        var audit = await (await admin.GetAsync($"/api/v1/audit-logs?entityType=Transporter&entityId={transporter.Id}")).ReadAsync<PagedResult<AuditLogDto>>();
        var raw = string.Concat(audit.Items.Select(l => l.Changes?.ToString()));
        raw.ShouldContain("BankAccountNumber", Case.Sensitive);
        raw.ShouldContain("••••••••9012");
        raw.ShouldNotContain("123456789012");
    }
}
