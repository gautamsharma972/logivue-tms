using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Tendering;

/// <summary>Pure tender rules: which states are open, which are final, and how a transporter may respond.</summary>
public static class TenderRules
{
    public static readonly TenderStatus[] ResponsiveStatuses = [TenderStatus.Sent, TenderStatus.Viewed];

    public static readonly TenderStatus[] FinalStatuses =
        [TenderStatus.Rejected, TenderStatus.Expired, TenderStatus.Withdrawn, TenderStatus.Awarded, TenderStatus.Cancelled];

    public static bool IsResponsive(TenderStatus status) => ResponsiveStatuses.Contains(status);

    public static bool IsFinal(TenderStatus status) => FinalStatuses.Contains(status);

    public static bool IsOpen(TenderStatus status) => status is TenderStatus.Draft or TenderStatus.Sent or TenderStatus.Viewed or TenderStatus.Accepted;

    public static void EnsureResponsive(TenderStatus status, string action) =>
        Ensure(IsResponsive(status), status, action);

    public static void Ensure(bool allowed, TenderStatus status, string action)
    {
        if (!allowed)
        {
            throw new BusinessRuleException($"A tender in status {status} cannot be {action}.", "ILLEGAL_TRANSITION");
        }
    }

    /// <summary>The next sequential invitation after <paramref name="sequence"/>, if one is still waiting to be contacted.</summary>
    public static int? NextSequence(IEnumerable<(int? Sequence, TenderStatus Status)> group, int? current)
    {
        if (current is not { } seq)
        {
            return null;
        }

        return group
            .Where(g => g.Sequence > seq && g.Status == TenderStatus.Draft)
            .Select(g => g.Sequence)
            .OrderBy(s => s)
            .FirstOrDefault();
    }
}
