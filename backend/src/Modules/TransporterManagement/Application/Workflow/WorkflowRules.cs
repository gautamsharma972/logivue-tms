using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Configuration;

namespace LogiVue.Tms.TransporterManagement.Application.Workflow;

/// <summary>
/// Pure workflow rules. Review stages and their approvers come from the configured steps, so the
/// order and number of approval levels can change without touching this code.
/// </summary>
internal static class WorkflowRules
{
    /// <summary>Submitting moves a transporter to <see cref="TransporterStatus.Submitted"/>; accepting it for review enters the first step.</summary>
    public static bool IsReviewStage(TransporterStatus status, IReadOnlyList<OnboardingStep> steps) =>
        steps.Any(s => s.Status == status);

    public static TransporterStatus NextStage(TransporterStatus current, IReadOnlyList<OnboardingStep> steps)
    {
        if (current == TransporterStatus.Submitted)
        {
            return steps.Count == 0 ? TransporterStatus.Approved : steps[0].Status;
        }

        var index = steps.ToList().FindIndex(s => s.Status == current);
        if (index < 0)
        {
            throw Illegal(current, "advance");
        }

        return index + 1 < steps.Count ? steps[index + 1].Status : TransporterStatus.Approved;
    }

    /// <summary>Who may advance or reject a transporter in <paramref name="current"/>.</summary>
    public static string ApproverRole(TransporterStatus current, IReadOnlyList<OnboardingStep> steps) =>
        current == TransporterStatus.Submitted
            ? Roles.TransportManager
            : steps.Single(s => s.Status == current).RequiredRole;

    public static void EnsureRole(ICurrentUser user, string requiredRole, string action)
    {
        if (!user.IsInRole(requiredRole) && !user.IsInRole(Roles.TransportAdmin))
        {
            throw new ForbiddenException($"Only {requiredRole} can {action} at this stage.", "WORKFLOW_ROLE_REQUIRED");
        }
    }

    public static void EnsureAllowedFrom(TransporterStatus current, string action, params TransporterStatus[] allowed)
    {
        if (!allowed.Contains(current))
        {
            throw Illegal(current, action);
        }
    }

    public static BusinessRuleException Illegal(TransporterStatus current, string action) =>
        new($"A transporter in status {current} cannot be {action}.", "ILLEGAL_TRANSITION");
}
