using Tms.Modules.Shipments.Domain;

namespace Tms.UnitTests.Shipments;

public class PlanningRunLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 9, 0, 0, TimeSpan.FromMinutes(330));

    private static PlanningRun Running() =>
        PlanningRun.CreateRunning(Guid.NewGuid(), Guid.NewGuid(), "PLN-1", 1, new DateOnly(2026, 10, 4), new PlanOptions(), [Guid.NewGuid()], null, Now);

    private static PlanSnapshot Plan(int unplanned = 0) =>
        PlanSnapshot.Build(SolverStatus.Feasible, "ok", [], Enumerable.Range(0, unplanned).Select(_ => new UnplannedOrder(Guid.NewGuid(), "O", UnplannedCodes.NoRate, "x", [])).ToList());

    [Fact]
    public void A_running_run_has_no_plan_cannot_be_approved_or_locked_and_logs_that_it_was_queued()
    {
        var run = Running();

        run.Status.ShouldBe(PlanStatus.Running);
        run.StartedAt.ShouldBe(Now);
        run.Log.Single().Message.ShouldContain("Queued");
        run.Approve(null, Now).IsFailure.ShouldBeTrue();
        run.SetLock(Guid.NewGuid(), true).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Finishing_sets_the_status_from_the_plan_and_only_once()
    {
        var run = Running();

        run.Complete(Plan(unplanned: 1), Now.AddSeconds(5)).IsSuccess.ShouldBeTrue();

        run.Status.ShouldBe(PlanStatus.PartiallyPlanned);
        run.CompletedAt.ShouldBe(Now.AddSeconds(5));
        run.Complete(Plan(), Now).IsFailure.ShouldBeTrue();
        run.Fail("late", Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_run_cancelled_while_running_cannot_then_be_completed()
    {
        var run = Running();

        run.Cancel("Not needed").IsSuccess.ShouldBeTrue();

        run.Status.ShouldBe(PlanStatus.Cancelled);
        run.Complete(Plan(), Now).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void A_failed_run_records_why_and_is_not_reviewable()
    {
        var run = Running();

        run.Fail("Planning failed: boom", Now.AddSeconds(2)).IsSuccess.ShouldBeTrue();

        run.Status.ShouldBe(PlanStatus.Infeasible);
        run.Plan.SolverStatus.ShouldBe(SolverStatus.Failed);
        run.Log[^1].Message.ShouldContain("boom");
        run.Approve(null, Now).IsFailure.ShouldBeTrue();
    }
}
