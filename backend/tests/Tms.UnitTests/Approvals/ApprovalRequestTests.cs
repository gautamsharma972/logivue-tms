using Tms.Modules.Approvals.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;

namespace Tms.UnitTests.Approvals;

public class ApprovalRequestTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Requester = Guid.NewGuid();
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Bob = Guid.NewGuid();
    private static readonly Guid Carol = Guid.NewGuid();

    private static ApprovalRequest TwoStep() =>
        ApprovalRequest.Submit(Tenant, "spot_rate", Guid.NewGuid(), "Spot rate Mumbai-Pune", 120_000m, Requester,
            [new PolicyStep("Manager", "m.approve", null), new PolicyStep("Director", "d.approve", 100_000m)], Now);

    private static ApprovalCompleted[] Completed(ApprovalRequest r) => r.DomainEvents.OfType<ApprovalCompleted>().ToArray();

    [Fact]
    public void Submit_StartsAtTheFirstStep_AndWaits()
    {
        var request = TwoStep();

        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.CurrentStepIndex.ShouldBe(0);
        request.CurrentPermission.ShouldBe("m.approve");
        request.Steps.Select(s => s.Status).ShouldAllBe(s => s == StepStatus.Pending);
        Completed(request).ShouldBeEmpty();
    }

    [Fact]
    public void Submit_WithNoApplicableSteps_IsApprovedImmediatelyAndAnnouncedOnce()
    {
        var request = ApprovalRequest.Submit(Tenant, "spot_rate", Guid.NewGuid(), "Tiny", 10m, Requester, [], Now);

        request.Status.ShouldBe(ApprovalStatus.Approved);
        request.CurrentStepIndex.ShouldBeNull();
        request.CompletedAt.ShouldBe(Now);
        Completed(request).ShouldHaveSingleItem().Outcome.ShouldBe(ApprovalStatus.Approved);
    }

    [Fact]
    public void ApprovingEveryStepInOrder_CompletesTheRequest()
    {
        var request = TwoStep();

        request.Approve(Alice, null, "ok", Now).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(ApprovalStatus.Pending);
        request.CurrentStepIndex.ShouldBe(1);
        request.CurrentPermission.ShouldBe("d.approve");
        Completed(request).ShouldBeEmpty("only the final decision is announced");

        request.Approve(Bob, null, null, Now.AddHours(1)).IsSuccess.ShouldBeTrue();
        request.Status.ShouldBe(ApprovalStatus.Approved);
        request.CurrentStepIndex.ShouldBeNull();
        request.CurrentPermission.ShouldBeNull();
        request.CompletedAt.ShouldBe(Now.AddHours(1));
        request.Steps.Select(s => s.DecidedBy).ShouldBe([Alice, Bob]);
        Completed(request).ShouldHaveSingleItem().Outcome.ShouldBe(ApprovalStatus.Approved);
    }

    [Fact]
    public void TheRequester_CanNeverDecideTheirOwnRequest_EvenOnBehalfOfSomeone()
    {
        var request = TwoStep();

        request.Approve(Requester, null, null, Now).Error.Code.ShouldBe("approvals.self_approval");
        request.Approve(Alice, Requester, null, Now).Error.Code.ShouldBe("approvals.self_approval"); // borrowing the requester's authority
        request.Status.ShouldBe(ApprovalStatus.Pending);
    }

    [Fact]
    public void ThePersonWhoApprovedAnEarlierStep_CannotApproveALaterOne()
    {
        var request = TwoStep();
        request.Approve(Alice, null, null, Now);

        var again = request.Approve(Alice, null, null, Now);

        again.IsFailure.ShouldBeTrue();
        again.Error.Code.ShouldBe("approvals.already_decided");
        request.CurrentStepIndex.ShouldBe(1);
    }

    [Fact]
    public void ADelegate_CannotApproveASecondStepEitherDirectlyOrForTheSamePrincipal()
    {
        var request = TwoStep();
        request.Approve(Alice, onBehalfOf: Bob, null, Now); // Alice acts for Bob

        request.Approve(Bob, null, null, Now).Error.Code.ShouldBe("approvals.already_decided");
        request.Approve(Carol, onBehalfOf: Alice, null, Now).Error.Code.ShouldBe("approvals.already_decided");
        request.Steps[0].OnBehalfOf.ShouldBe(Bob);
    }

    [Fact]
    public void Reject_NeedsAReason_AndEndsTheRequest()
    {
        var request = TwoStep();

        request.Reject(Alice, null, "  ", Now).Error.Code.ShouldBe("approvals.comment_required");
        request.Status.ShouldBe(ApprovalStatus.Pending);

        request.Reject(Alice, null, "Rate too high", Now).IsSuccess.ShouldBeTrue();

        request.Status.ShouldBe(ApprovalStatus.Rejected);
        request.Steps[0].Status.ShouldBe(StepStatus.Rejected);
        request.Steps[0].Comment.ShouldBe("Rate too high");
        request.Steps[1].Status.ShouldBe(StepStatus.Pending, "later steps are never reached");
        Completed(request).ShouldHaveSingleItem().Outcome.ShouldBe(ApprovalStatus.Rejected);
    }

    [Fact]
    public void ACompletedRequest_CannotBeDecidedOrCancelled()
    {
        var request = TwoStep();
        request.Reject(Alice, null, "no", Now);

        request.Approve(Bob, null, null, Now).Error.Code.ShouldBe("approvals.not_pending");
        request.Reject(Bob, null, "no", Now).Error.Code.ShouldBe("approvals.not_pending");
        request.Cancel(Now).Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public void Cancel_ClosesAPendingRequest()
    {
        var request = TwoStep();

        request.Cancel(Now).IsSuccess.ShouldBeTrue();

        request.Status.ShouldBe(ApprovalStatus.Cancelled);
        request.CurrentPermission.ShouldBeNull();
        Completed(request).ShouldHaveSingleItem().Outcome.ShouldBe(ApprovalStatus.Cancelled);
    }

    [Fact]
    public void DeciderKey_RecordsEveryoneWhoActed_ForInboxQueries()
    {
        var request = TwoStep();
        request.Approve(Alice, onBehalfOf: Bob, null, Now);

        request.HasDecided(Alice).ShouldBeTrue();
        request.HasDecided(Bob).ShouldBeTrue();
        request.HasDecided(Carol).ShouldBeFalse();
        request.DeciderKey.ShouldContain(ApprovalRequest.KeyFor(Alice));
    }
}

public class DelegationTests
{
    private static readonly DateTimeOffset Now = new(2026, 5, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public void Create_RejectsSelfDelegationAndBadPeriods()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        Delegation.Create(Tenant, a, a, Now, Now.AddDays(1), null, Now).Error.Code.ShouldBe("approvals.delegate_self");
        Delegation.Create(Tenant, a, b, Now.AddDays(2), Now.AddDays(1), null, Now).Error.Code.ShouldBe("approvals.delegation_period");
        Delegation.Create(Tenant, a, b, Now.AddDays(-5), Now.AddDays(-1), null, Now).Error.Code.ShouldBe("approvals.delegation_expired");
    }

    [Fact]
    public void IsActiveAt_HonoursWindowAndRevocation()
    {
        var delegation = Delegation.Create(Tenant, Guid.NewGuid(), Guid.NewGuid(), Now.AddDays(1), Now.AddDays(3), "leave", Now).Value;

        delegation.IsActiveAt(Now).ShouldBeFalse("not started yet");
        delegation.IsActiveAt(Now.AddDays(2)).ShouldBeTrue();
        delegation.IsActiveAt(Now.AddDays(3)).ShouldBeFalse("end is exclusive");

        delegation.Revoke(Now.AddDays(2));
        delegation.IsActiveAt(Now.AddDays(2)).ShouldBeFalse();
    }
}
