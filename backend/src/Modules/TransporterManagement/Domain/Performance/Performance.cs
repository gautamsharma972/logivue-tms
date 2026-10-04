using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Domain.Performance;

/// <summary>
/// A KPI for a period, stored with its numerator and denominator so every value stays auditable.
/// <see cref="KpiValue"/> is null when the KPI is not measurable (never a silent zero).
/// </summary>
public class PerformanceKpi
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public long? LaneReference { get; set; }
    public long? VehicleTypeReference { get; set; }
    public string? ServiceType { get; set; }
    public KpiType KpiType { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal Numerator { get; set; }
    public decimal Denominator { get; set; }
    public decimal? KpiValue { get; set; }
    public int CalculationVersion { get; set; } = 1;
}

public class TransporterScorecard
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public string ScorecardProfile { get; set; } = "Default";
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public decimal? OverallScore { get; set; }
    public int? Rank { get; set; }
    public ScorecardStatus Status { get; set; } = ScorecardStatus.Generated;
    public DateTime GeneratedAt { get; set; }

    /// <summary>Version of the formula and weights used. Historical scorecards keep their version.</summary>
    public int CalculationVersion { get; set; } = 1;
}

/// <summary>Snapshot of one KPI inside a scorecard, with the weight in force at generation time.</summary>
public class ScorecardDetail
{
    public long Id { get; set; }
    public long ScorecardId { get; set; }
    public KpiType KpiType { get; set; }
    public decimal? KpiValue { get; set; }

    /// <summary>The pooled counts behind <see cref="KpiValue"/>, kept so the score can be audited.</summary>
    public decimal Numerator { get; set; }
    public decimal Denominator { get; set; }
    public decimal Weight { get; set; }
    public decimal? WeightedScore { get; set; }
    public decimal? BenchmarkValue { get; set; }
    public decimal? Trend { get; set; }
}

public class TransporterRanking
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public long? LaneReference { get; set; }
    public long? VehicleTypeReference { get; set; }
    public string? ServiceType { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int Rank { get; set; }
    public decimal Score { get; set; }
}
