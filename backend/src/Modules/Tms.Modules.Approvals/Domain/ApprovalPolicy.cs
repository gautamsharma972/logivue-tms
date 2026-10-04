using Tms.SharedKernel.Domain;
using Tms.SharedKernel.Results;

namespace Tms.Modules.Approvals.Domain;

/// <summary>One link in the approval chain: who may approve (by permission) and when the step applies.</summary>
/// <param name="MinAmount">The step applies only when the document amount is at least this. Null = always applies.</param>
public sealed record PolicyStep(string Name, string RequiredPermission, decimal? MinAmount);

/// <summary>
/// The approval matrix for one document type in one tenant: an ordered list of steps, each gated by a permission and
/// optionally by an amount threshold. Requests copy the applicable steps when submitted, so editing a policy never
/// changes requests already in flight.
/// </summary>
public sealed class ApprovalPolicy : AggregateRoot, ITenantScoped
{
    public const int MaxSteps = 10;

    private ApprovalPolicy()
    {
    }

    public Guid TenantId { get; private set; }

    public string DocumentType { get; private set; } = null!;

    public bool IsActive { get; private set; }

    public IReadOnlyList<PolicyStep> Steps { get; private set; } = [];

    public static Result<ApprovalPolicy> Create(Guid tenantId, string documentType, bool isActive, IEnumerable<PolicyStep> steps)
    {
        var policy = new ApprovalPolicy { TenantId = tenantId, DocumentType = documentType };
        var result = policy.Update(isActive, steps);
        return result.IsFailure ? result.Error : policy;
    }

    public Result Update(bool isActive, IEnumerable<PolicyStep> steps)
    {
        var list = steps.Select(s => s with { Name = s.Name.Trim(), RequiredPermission = s.RequiredPermission.Trim() }).ToList();

        if (list.Count is 0 or > MaxSteps)
        {
            return Error.Validation("approvals.policy_steps", $"A policy needs between 1 and {MaxSteps} steps.");
        }

        if (list.Any(s => s.Name.Length == 0 || s.RequiredPermission.Length == 0))
        {
            return Error.Validation("approvals.policy_step_incomplete", "Every step needs a name and a required permission.");
        }

        if (list.Any(s => s.MinAmount is < 0))
        {
            return Error.Validation("approvals.policy_threshold", "Thresholds cannot be negative.");
        }

        IsActive = isActive;
        Steps = list;
        return Result.Success();
    }

    /// <summary>The steps a document of this amount must pass through, in order. A missing amount counts as zero.</summary>
    public IReadOnlyList<PolicyStep> StepsFor(decimal? amount) =>
        Steps.Where(s => s.MinAmount is null || (amount ?? 0m) >= s.MinAmount).ToList();
}
