using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Approvals.Application;
using Tms.Modules.Approvals.Domain;
using Tms.Modules.Platform.Application.Audit;
using Tms.Modules.Platform.Application.Auth;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class ApprovalTests(TmsApiFactory factory)
{
    private const string L1 = TmsApiFactory.Approver1;
    private const string L2 = TmsApiFactory.Approver2;

    private sealed record Person(HttpClient Client, Guid Id) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }

    private async Task<Person> PersonAsync(HttpClient admin, params string[] permissions)
    {
        var client = await factory.UserWithPermissionsAsync(admin, permissions);
        var me = await (await client.GetAsync("/api/v1/auth/me")).ReadAsync<UserProfile>();
        return new Person(client, me.Id);
    }

    /// <summary>Replaces the tenant's policy for a document type (creating it on first use).</summary>
    private static async Task SetPolicyAsync(HttpClient admin, string documentType, params PolicyStepDto[] steps)
    {
        var all = await (await admin.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<PolicyDto>>();
        var version = all.Single(p => p.DocumentType == documentType).Version;

        var response = await admin.PutJsonAsync($"/api/v1/approvals/policies/{documentType}", new SavePolicyRequest(true, steps, version));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task<ApprovalSubmission> SubmitAsync(HttpClient who, string documentType, decimal? amount, Guid? documentId = null)
    {
        var response = await who.PostJsonAsync("/api/v1/dev/approvals/submit",
            new SubmitApproval(documentType, documentId ?? Guid.NewGuid(), $"Test {documentType}", amount));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.ReadAsync<ApprovalSubmission>();
    }

    private static async Task<RequestDto> GetAsync(HttpClient who, Guid id) =>
        await (await who.GetAsync($"/api/v1/approvals/requests/{id}")).ReadAsync<RequestDto>();

    private static Task<HttpResponseMessage> ApproveAsync(HttpClient who, Guid id, string? comment = null) =>
        who.PostJsonAsync($"/api/v1/approvals/requests/{id}/approve", new DecisionRequest(comment));

    private static async Task<List<Guid>> InboxAsync(HttpClient who) =>
        (await (await who.GetAsync("/api/v1/approvals/requests?scope=inbox&pageSize=200")).ReadAsync<PagedResult<RequestSummaryDto>>())
        .Items.Select(r => r.Id).ToList();

    [Fact]
    public async Task Policies_are_admin_only_and_validated()
    {
        using var admin = await factory.AdminAsync();
        using var nobody = await PersonAsync(admin);

        (await nobody.Client.GetAsync("/api/v1/approvals/policies")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var unknownPermission = await admin.PutJsonAsync("/api/v1/approvals/policies/claim",
            new SavePolicyRequest(true, [new PolicyStepDto("Boss", "does.not.exist", null)], null));
        unknownPermission.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await unknownPermission.ProblemCodeAsync()).ShouldBe("approvals.unknown_permission");

        var unknownType = await admin.PutJsonAsync("/api/v1/approvals/policies/not_a_type",
            new SavePolicyRequest(true, [new PolicyStepDto("Boss", L1, null)], null));
        unknownType.StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var noSteps = await admin.PutJsonAsync("/api/v1/approvals/policies/claim", new SavePolicyRequest(true, [], null));
        noSteps.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Saving_an_existing_policy_needs_the_current_version()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "claim", new PolicyStepDto("Reviewer", L1, null));
        var current = (await (await admin.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<PolicyDto>>()).Single(p => p.DocumentType == "claim");
        var steps = new[] { new PolicyStepDto("Reviewer", L1, null) };

        (await admin.PutJsonAsync("/api/v1/approvals/policies/claim", new SavePolicyRequest(true, steps, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var edited = new[] { new PolicyStepDto("Senior reviewer", L1, null) }; // a real change, so a new version is created
        (await admin.PutJsonAsync("/api/v1/approvals/policies/claim", new SavePolicyRequest(true, edited, current.Version)))
            .StatusCode.ShouldBe(HttpStatusCode.OK);

        var stale = await admin.PutJsonAsync("/api/v1/approvals/policies/claim", new SavePolicyRequest(true, steps, current.Version));
        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submitting_without_an_active_policy_is_refused_rather_than_silently_approved()
    {
        using var admin = await factory.AdminAsync();
        using var requester = await PersonAsync(admin);

        // Make the situation explicit instead of relying on shared state: a policy exists but is switched off.
        var all = await (await admin.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<PolicyDto>>();
        var current = all.Single(p => p.DocumentType == "transporter_onboarding");
        var switchedOff = await admin.PutJsonAsync("/api/v1/approvals/policies/transporter_onboarding",
            new SavePolicyRequest(false, [new PolicyStepDto("Reviewer", L1, null)], current.Version));
        switchedOff.StatusCode.ShouldBe(HttpStatusCode.OK, await switchedOff.Content.ReadAsStringAsync());

        var response = await requester.Client.PostJsonAsync("/api/v1/dev/approvals/submit",
            new SubmitApproval("transporter_onboarding", Guid.NewGuid(), "New vendor", null));

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ProblemCodeAsync()).ShouldBe("approvals.no_policy");
    }

    [Fact]
    public async Task Thresholds_decide_how_many_steps_a_request_needs()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "spot_rate", new PolicyStepDto("Level 1", L1, null), new PolicyStepDto("Level 2", L2, 100_000m));
        using var requester = await PersonAsync(admin);
        using var approver1 = await PersonAsync(admin, L1);
        using var approver2 = await PersonAsync(admin, L2);

        // Small: one step. Approver 1 finishes it.
        var small = await SubmitAsync(requester.Client, "spot_rate", 5_000m);
        (await GetAsync(requester.Client, small.RequestId)).Steps.Count.ShouldBe(1);
        (await ApproveAsync(approver1.Client, small.RequestId, "fine")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(requester.Client, small.RequestId)).Status.ShouldBe(ApprovalStatus.Approved);

        // Large: two steps in order. Approver 2 cannot jump the queue.
        var large = await SubmitAsync(requester.Client, "spot_rate", 250_000m);
        (await GetAsync(requester.Client, large.RequestId)).Steps.Count.ShouldBe(2);
        (await ApproveAsync(approver2.Client, large.RequestId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        (await ApproveAsync(approver1.Client, large.RequestId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        var midway = await GetAsync(requester.Client, large.RequestId);
        midway.Status.ShouldBe(ApprovalStatus.Pending);
        midway.CurrentStepIndex.ShouldBe(1);

        (await ApproveAsync(approver1.Client, large.RequestId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await ApproveAsync(approver2.Client, large.RequestId, "ok")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var done = await GetAsync(requester.Client, large.RequestId);
        done.Status.ShouldBe(ApprovalStatus.Approved);
        done.Steps.Select(s => s.DecidedByName).ShouldAllBe(n => n != null);
        done.CompletedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Amounts_below_every_threshold_are_approved_instantly()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "freight_bill", new PolicyStepDto("Finance", L2, 10_000m));
        using var requester = await PersonAsync(admin);

        var submission = await SubmitAsync(requester.Client, "freight_bill", 500m);

        submission.Status.ShouldBe(ApprovalStatus.Approved);
    }

    [Fact]
    public async Task Segregation_of_duties_is_enforced()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "freight_contract", new PolicyStepDto("Level 1", L1, null), new PolicyStepDto("Level 2", L2, null));
        using var both = await PersonAsync(admin, L1, L2); // holds both permissions
        using var requester = await PersonAsync(admin, L1);

        // Requesters cannot approve what they submitted, even though they hold the permission.
        var own = await SubmitAsync(requester.Client, "freight_contract", 1m);
        var self = await ApproveAsync(requester.Client, own.RequestId);
        self.StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // One person cannot approve two steps of the same request.
        var chain = await SubmitAsync(requester.Client, "freight_contract", 1m);
        (await ApproveAsync(both.Client, chain.RequestId)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ApproveAsync(both.Client, chain.RequestId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await GetAsync(requester.Client, chain.RequestId)).Status.ShouldBe(ApprovalStatus.Pending);
    }

    [Fact]
    public async Task Rejection_requires_a_reason_ends_the_request_and_is_announced()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "claim", new PolicyStepDto("Reviewer", L1, null));
        using var requester = await PersonAsync(admin);
        using var approver = await PersonAsync(admin, L1);
        var submission = await SubmitAsync(requester.Client, "claim", 900m);

        var noReason = await approver.Client.PostJsonAsync($"/api/v1/approvals/requests/{submission.RequestId}/reject", new DecisionRequest(null));
        noReason.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await noReason.ProblemCodeAsync()).ShouldBe("approvals.comment_required");

        var rejected = await approver.Client.PostJsonAsync($"/api/v1/approvals/requests/{submission.RequestId}/reject", new DecisionRequest("Photos missing"));
        rejected.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await rejected.ReadAsync<RequestDto>()).Status.ShouldBe(ApprovalStatus.Rejected);

        var late = await ApproveAsync(approver.Client, submission.RequestId);
        late.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var log = factory.Services.GetRequiredService<ApprovalEventLog>();
        log.Events.ShouldContain(e => e.RequestId == submission.RequestId && e.Outcome == ApprovalStatus.Rejected);
    }

    [Fact]
    public async Task Completion_is_published_for_other_modules_exactly_once()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "spot_rate", new PolicyStepDto("Level 1", L1, null));
        using var requester = await PersonAsync(admin);
        using var approver = await PersonAsync(admin, L1);
        var documentId = Guid.NewGuid();
        var submission = await SubmitAsync(requester.Client, "spot_rate", 10m, documentId);

        await ApproveAsync(approver.Client, submission.RequestId);

        factory.Services.GetRequiredService<ApprovalEventLog>().Events
            .Where(e => e.RequestId == submission.RequestId)
            .ShouldHaveSingleItem()
            .ShouldSatisfyAllConditions(
                e => e.DocumentId.ShouldBe(documentId),
                e => e.DocumentType.ShouldBe("spot_rate"),
                e => e.Outcome.ShouldBe(ApprovalStatus.Approved));
    }

    [Fact]
    public async Task A_document_cannot_have_two_approvals_in_flight()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "claim", new PolicyStepDto("Reviewer", L1, null));
        using var requester = await PersonAsync(admin);
        var documentId = Guid.NewGuid();
        await SubmitAsync(requester.Client, "claim", 1m, documentId);

        var again = await requester.Client.PostJsonAsync("/api/v1/dev/approvals/submit", new SubmitApproval("claim", documentId, "Again", 1m));

        again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await again.ProblemCodeAsync()).ShouldBe("approvals.already_pending");
    }

    [Fact]
    public async Task The_inbox_only_contains_requests_the_user_can_act_on()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "claim", new PolicyStepDto("Reviewer", L1, null));
        using var requester = await PersonAsync(admin, L1);
        using var approver = await PersonAsync(admin, L1);
        using var outsider = await PersonAsync(admin, L2);
        var submission = await SubmitAsync(requester.Client, "claim", 1m);

        (await InboxAsync(approver.Client)).ShouldContain(submission.RequestId);
        (await InboxAsync(outsider.Client)).ShouldNotContain(submission.RequestId);
        (await InboxAsync(requester.Client)).ShouldNotContain(submission.RequestId); // own request, even with the permission

        await ApproveAsync(approver.Client, submission.RequestId);
        (await InboxAsync(approver.Client)).ShouldNotContain(submission.RequestId);

        // "mine" lists what I submitted; "all" is restricted.
        var mine = await (await requester.Client.GetAsync("/api/v1/approvals/requests?scope=mine&pageSize=200")).ReadAsync<PagedResult<RequestSummaryDto>>();
        mine.Items.ShouldContain(r => r.Id == submission.RequestId);
        (await outsider.Client.GetAsync("/api/v1/approvals/requests?scope=all")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/v1/approvals/requests?scope=all")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_delegate_acts_with_the_delegators_authority_until_it_is_revoked()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "claim", new PolicyStepDto("Reviewer", L1, null));
        using var requester = await PersonAsync(admin);
        using var approver = await PersonAsync(admin, L1);
        using var cover = await PersonAsync(admin); // holds no permissions at all
        var submission = await SubmitAsync(requester.Client, "claim", 1m);

        // Before the delegation the cover has no authority.
        (await InboxAsync(cover.Client)).ShouldNotContain(submission.RequestId);
        (await ApproveAsync(cover.Client, submission.RequestId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var now = DateTimeOffset.UtcNow;
        var created = await approver.Client.PostJsonAsync("/api/v1/approvals/delegations",
            new CreateDelegationRequest(cover.Id, now.AddMinutes(-1), now.AddDays(2), "On leave"));
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var delegation = await created.ReadAsync<DelegationDto>();
        delegation.IsActive.ShouldBeTrue();

        (await InboxAsync(cover.Client)).ShouldContain(submission.RequestId);
        var detail = await GetAsync(cover.Client, submission.RequestId);
        detail.CanDecide.ShouldBeTrue();

        (await ApproveAsync(cover.Client, submission.RequestId, "covering")).StatusCode.ShouldBe(HttpStatusCode.OK);
        var done = await GetAsync(requester.Client, submission.RequestId);
        done.Status.ShouldBe(ApprovalStatus.Approved);
        done.Steps[0].DecidedBy.ShouldBe(cover.Id);
        done.Steps[0].OnBehalfOf.ShouldBe(approver.Id);

        // Revocation takes effect immediately for new work.
        var second = await SubmitAsync(requester.Client, "claim", 1m);
        (await approver.Client.DeleteAsync($"/api/v1/approvals/delegations/{delegation.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await InboxAsync(cover.Client)).ShouldNotContain(second.RequestId);
        (await ApproveAsync(cover.Client, second.RequestId)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        // Only the delegator can revoke.
        (await cover.Client.DeleteAsync($"/api/v1/approvals/delegations/{delegation.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delegations_are_validated()
    {
        using var admin = await factory.AdminAsync();
        using var person = await PersonAsync(admin, L1);
        var now = DateTimeOffset.UtcNow;

        (await person.Client.PostJsonAsync("/api/v1/approvals/delegations", new CreateDelegationRequest(person.Id, now, now.AddDays(1), null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await person.Client.PostJsonAsync("/api/v1/approvals/delegations", new CreateDelegationRequest(Guid.NewGuid(), now, now.AddDays(1), null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_the_requester_or_an_approvals_admin_can_cancel_and_only_while_pending()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "claim", new PolicyStepDto("Reviewer", L1, null));
        using var requester = await PersonAsync(admin);
        using var bystander = await PersonAsync(admin, L1);
        var submission = await SubmitAsync(requester.Client, "claim", 1m);

        (await bystander.Client.PostAsync($"/api/v1/approvals/requests/{submission.RequestId}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var cancelled = await requester.Client.PostAsync($"/api/v1/approvals/requests/{submission.RequestId}/cancel", null);
        cancelled.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cancelled.ReadAsync<RequestDto>()).Status.ShouldBe(ApprovalStatus.Cancelled);

        (await requester.Client.PostAsync($"/api/v1/approvals/requests/{submission.RequestId}/cancel", null)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await ApproveAsync(bystander.Client, submission.RequestId)).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Strangers_cannot_see_a_request_and_other_tenants_cannot_even_tell_it_exists()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "claim", new PolicyStepDto("Reviewer", L1, null));
        using var requester = await PersonAsync(admin);
        using var stranger = await PersonAsync(admin);
        using var acme = await factory.SignedInAsync(TmsApiFactory.OtherTenant, TmsApiFactory.OtherAdminEmail, TmsApiFactory.OtherAdminPassword);
        var submission = await SubmitAsync(requester.Client, "claim", 1m);

        (await stranger.Client.GetAsync($"/api/v1/approvals/requests/{submission.RequestId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await acme.GetAsync($"/api/v1/approvals/requests/{submission.RequestId}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await ApproveAsync(acme, submission.RequestId)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var acmeAll = await (await acme.GetAsync("/api/v1/approvals/requests?scope=all&pageSize=200")).ReadAsync<PagedResult<RequestSummaryDto>>();
        acmeAll.Items.ShouldNotContain(r => r.Id == submission.RequestId);

        // The other tenant's own policies are independent of ours.
        var acmePolicies = await (await acme.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<PolicyDto>>();
        acmePolicies.ShouldAllBe(p => !p.IsConfigured);
    }

    [Fact]
    public async Task Every_decision_lands_in_the_audit_trail()
    {
        using var admin = await factory.AdminAsync();
        await SetPolicyAsync(admin, "claim", new PolicyStepDto("Reviewer", L1, null));
        using var requester = await PersonAsync(admin);
        using var approver = await PersonAsync(admin, L1);
        var submission = await SubmitAsync(requester.Client, "claim", 1m);
        await ApproveAsync(approver.Client, submission.RequestId, "good");

        var logs = await (await admin.GetAsync($"/api/v1/audit-logs?entityType=ApprovalRequest&entityId={submission.RequestId}"))
            .ReadAsync<PagedResult<AuditLogDto>>();

        logs.Items.Select(l => l.Action).ShouldBe(["Updated", "Created"]);
        logs.Items[0].UserName.ShouldNotBeNullOrEmpty();
        logs.Items[0].Changes!.Value.GetProperty("Status").GetProperty("new").GetString().ShouldBe("Approved");
    }
}
