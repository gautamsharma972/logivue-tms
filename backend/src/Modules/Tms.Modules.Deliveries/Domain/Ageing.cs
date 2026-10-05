namespace Tms.Modules.Deliveries.Domain;

public enum AgeingStage
{
    /// <summary>Delivered, and no proof has been submitted yet. The clock runs from the delivery.</summary>
    PendingSubmission = 1,

    /// <summary>Submitted and waiting for a decision. The clock runs from the submission.</summary>
    PendingReview = 2,

    /// <summary>Rejected: the transporter has to correct it. The clock runs from the rejection.</summary>
    Rejected = 3,

    /// <summary>More evidence was asked for. The clock runs from the request.</summary>
    ResubmissionRequired = 4,
}

/// <summary>How long things have waited, in the buckets a tenant chose, against the targets it set. Timestamps are exact: ageing is never worked out from dates.</summary>
public static class Ageing
{
    public static IReadOnlyList<string> Labels(AgeingSetting setting)
    {
        var uppers = Normalise(setting);
        var labels = new List<string>();
        var from = 0;
        foreach (var upper in uppers)
        {
            labels.Add(from == upper ? $"{upper} day" : $"{from}–{upper} days");
            from = upper + 1;
        }

        labels.Add($">{uppers[^1]} days");
        return labels;
    }

    /// <summary>The bucket for an age in hours: whole days waited, so 47 hours is still "1 day".</summary>
    public static int BucketIndex(double hours, AgeingSetting setting)
    {
        var uppers = Normalise(setting);
        var days = (int)Math.Floor(Math.Max(0, hours) / 24);
        for (var i = 0; i < uppers.Count; i++)
        {
            if (days <= uppers[i])
            {
                return i;
            }
        }

        return uppers.Count;
    }

    public static double Hours(DateTimeOffset from, DateTimeOffset to) => Math.Max(0, (to - from).TotalHours);

    /// <summary>The target for a stage, in hours.</summary>
    public static int TargetHours(AgeingStage stage, SlaSetting sla) => stage switch
    {
        AgeingStage.PendingSubmission => sla.PodSubmissionHours,
        AgeingStage.PendingReview => sla.PodReviewHours,
        _ => sla.ResubmissionHours,
    };

    private static List<int> Normalise(AgeingSetting setting)
    {
        var uppers = setting.UpperDays.Where(d => d >= 0).Distinct().Order().ToList();
        return uppers.Count == 0 ? [1, 3, 7, 15, 30] : uppers;
    }
}
