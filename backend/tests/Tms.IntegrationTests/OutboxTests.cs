using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tms.IntegrationTests.Infrastructure;
using Tms.Modules.Approvals.Application;
using Tms.Modules.Platform.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Persistence;

namespace Tms.IntegrationTests;

[Collection(ApiCollection.Name)]
public class OutboxTests(TmsApiFactory factory)
{
    private const string L1 = TmsApiFactory.Approver1;

    private async Task<List<OutboxMessage>> MessagesForAsync(Guid documentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PlatformDbContext>();
        var all = await db.OutboxMessages.AsNoTracking().Where(m => m.Type.EndsWith(nameof(ApprovalCompleted))).ToListAsync();
        return all.Where(m => m.Payload.Contains(documentId.ToString(), StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> read, Func<T, bool> done, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        T value;
        do
        {
            value = await read();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(300);
        }
        while (DateTime.UtcNow < deadline);

        return value;
    }

    private async Task<(Guid DocumentId, Guid RequestId, HttpClient Approver, HttpClient Admin)> PendingRequestAsync()
    {
        var admin = await factory.AdminAsync();
        var all = await (await admin.GetAsync("/api/v1/approvals/policies")).ReadAsync<List<PolicyDto>>();
        await admin.PutJsonAsync("/api/v1/approvals/policies/claim",
            new SavePolicyRequest(true, [new PolicyStepDto("Reviewer", L1, null)], all.Single(p => p.DocumentType == "claim").Version));
        var requester = await factory.UserWithPermissionsAsync(admin);
        var approver = await factory.UserWithPermissionsAsync(admin, L1);
        var documentId = Guid.NewGuid();
        var submit = await requester.PostJsonAsync("/api/v1/dev/approvals/submit", new SubmitApproval("claim", documentId, "Outbox test", 1m));
        var submission = await submit.ReadAsync<ApprovalSubmission>();
        return (documentId, submission.RequestId, approver, admin);
    }

    [Fact]
    public async Task A_delivered_event_is_recorded_and_marked_processed()
    {
        var (documentId, requestId, approver, admin) = await PendingRequestAsync();
        using var _ = approver;
        using var __ = admin;

        (await approver.PostJsonAsync($"/api/v1/approvals/requests/{requestId}/approve", new DecisionRequest(null))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var messages = await MessagesForAsync(documentId);
        var message = messages.ShouldHaveSingleItem();
        message.ProcessedAt.ShouldNotBeNull("inline delivery succeeded");
        message.Attempts.ShouldBe(0);
        message.TenantId.ShouldNotBeNull();
        message.UserId.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_subscriber_that_fails_is_retried_by_the_worker_as_the_original_tenant_and_user()
    {
        var (documentId, requestId, approver, admin) = await PendingRequestAsync();
        using var _ = approver;
        using var __ = admin;
        var state = factory.Services.GetRequiredService<FlakySubscriberState>();
        state.FailuresRemaining[documentId] = 2; // fail inline, fail once in the worker, then succeed

        var response = await approver.PostJsonAsync($"/api/v1/approvals/requests/{requestId}/approve", new DecisionRequest(null));
        response.StatusCode.ShouldBe(HttpStatusCode.OK, "a subscriber failure must not fail the user's request");

        var message = (await EventuallyAsync(() => MessagesForAsync(documentId), m => m.Count == 1 && m[0].ProcessedAt != null, 30)).ShouldHaveSingleItem();

        message.ProcessedAt.ShouldNotBeNull("the worker eventually delivered it");
        message.Attempts.ShouldBeGreaterThanOrEqualTo(2);
        state.Seen[documentId].Attempts.ShouldBe(3);
        var (_, tenant, user) = state.Seen[documentId];
        tenant.ShouldBe(message.TenantId, "retries run inside the originating tenant");
        user.ShouldBe(message.UserId, "and are attributed to the originating user");
    }

    [Fact]
    public async Task A_subscriber_that_never_recovers_is_dead_lettered_not_retried_forever()
    {
        var (documentId, requestId, approver, admin) = await PendingRequestAsync();
        using var _ = approver;
        using var __ = admin;
        var state = factory.Services.GetRequiredService<FlakySubscriberState>();
        state.FailuresRemaining[documentId] = int.MaxValue;

        (await approver.PostJsonAsync($"/api/v1/approvals/requests/{requestId}/approve", new DecisionRequest(null))).StatusCode.ShouldBe(HttpStatusCode.OK);

        var message = (await EventuallyAsync(() => MessagesForAsync(documentId), m => m.Count == 1 && m[0].DeadLetteredAt != null, 40)).ShouldHaveSingleItem();

        message.DeadLetteredAt.ShouldNotBeNull();
        message.ProcessedAt.ShouldBeNull();
        message.LastError.ShouldNotBeNull().ShouldContain("Simulated subscriber failure");
        state.Seen[documentId].Attempts.ShouldBe(3, "MaxAttempts is 3 in the test host");
    }
}
