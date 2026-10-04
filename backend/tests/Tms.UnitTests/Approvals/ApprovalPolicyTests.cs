using Tms.Modules.Approvals.Domain;
using Tms.SharedKernel.Results;

namespace Tms.UnitTests.Approvals;

public class ApprovalPolicyTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static ApprovalPolicy NewPolicy(params PolicyStep[] steps) =>
        ApprovalPolicy.Create(Tenant, "spot_rate", true, steps).Value;

    [Fact]
    public void StepsFor_AppliesOnlyStepsWhoseThresholdIsReached()
    {
        var policy = NewPolicy(
            new PolicyStep("Supervisor", "a.approve", null),
            new PolicyStep("Manager", "b.approve", 50_000m),
            new PolicyStep("Director", "c.approve", 500_000m));

        policy.StepsFor(1_000m).Select(s => s.Name).ShouldBe(["Supervisor"]);
        policy.StepsFor(50_000m).Select(s => s.Name).ShouldBe(["Supervisor", "Manager"]); // threshold is inclusive
        policy.StepsFor(900_000m).Select(s => s.Name).ShouldBe(["Supervisor", "Manager", "Director"]);
        policy.StepsFor(null).Select(s => s.Name).ShouldBe(["Supervisor"]);
    }

    [Fact]
    public void StepsFor_PreservesConfiguredOrder_NotThresholdOrder()
    {
        var policy = NewPolicy(new PolicyStep("Finance", "f.approve", 10m), new PolicyStep("Ops", "o.approve", null));

        policy.StepsFor(100m).Select(s => s.Name).ShouldBe(["Finance", "Ops"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void Create_RejectsAnInvalidNumberOfSteps(int count)
    {
        var steps = Enumerable.Range(0, count).Select(i => new PolicyStep($"S{i}", "x.approve", null));

        var result = ApprovalPolicy.Create(Tenant, "spot_rate", true, steps);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Create_RejectsIncompleteStepsAndNegativeThresholds()
    {
        ApprovalPolicy.Create(Tenant, "spot_rate", true, [new PolicyStep(" ", "x.approve", null)]).IsFailure.ShouldBeTrue();
        ApprovalPolicy.Create(Tenant, "spot_rate", true, [new PolicyStep("S", "", null)]).IsFailure.ShouldBeTrue();
        ApprovalPolicy.Create(Tenant, "spot_rate", true, [new PolicyStep("S", "x.approve", -1m)]).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void Update_ReplacesStepsAndTrimsNames()
    {
        var policy = NewPolicy(new PolicyStep("Old", "x.approve", null));

        policy.Update(false, [new PolicyStep("  New ", " y.approve ", 5m)]).IsSuccess.ShouldBeTrue();

        policy.IsActive.ShouldBeFalse();
        policy.Steps.ShouldHaveSingleItem().ShouldBe(new PolicyStep("New", "y.approve", 5m));
    }
}
