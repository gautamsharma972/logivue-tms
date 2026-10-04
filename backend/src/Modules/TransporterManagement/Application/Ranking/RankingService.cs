using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Scorecards;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;

namespace LogiVue.Tms.TransporterManagement.Application.Ranking;

public enum RankingMetric
{
    OverallScore,
    OnTimePickup,
    OnTimeDelivery,
    PlacementCompliance,
    TenderAcceptance,
    PodCompliance,
    ClaimsRate,
    CostPerformance,
    Availability
}

/// <summary>
/// Where the KPIs come from. No lane and no vehicle type means transporter-level KPIs. Lane filters need both origin and
/// destination. Region is the transporter's state. Transporter category is its transporter type.
/// </summary>
public sealed record RankingScope(
    DateOnly From,
    DateOnly To,
    long? OriginLocationReference = null,
    long? DestinationLocationReference = null,
    string? ServiceType = null,
    long? VehicleTypeReference = null,
    string? Region = null,
    long? TransporterTypeId = null);

public sealed record RankedKpiDto(KpiType Kpi, decimal? Value, decimal Numerator, decimal Denominator);

/// <param name="Rank">1 is best. Null for transporters that are not ranked.</param>
/// <param name="MetricValue">The value the ranking is sorted on: the overall score, or the chosen KPI's percentage.</param>
/// <param name="Note">Why a transporter is not ranked, such as a sample below the minimum.</param>
public sealed record RankedTransporterDto(
    int? Rank,
    long TransporterId,
    string TransporterCode,
    string TransporterName,
    string? Region,
    TransporterStatus Status,
    decimal? MetricValue,
    decimal? OverallScore,
    bool Ranked,
    string? Note,
    IReadOnlyList<RankedKpiDto> Kpis);

public sealed record RankingResultDto(RankingMetric Metric, string ScopeLabel, DateOnly From, DateOnly To, IReadOnlyList<RankedTransporterDto> Rows);

/// <summary>
/// One benchmark line. <see cref="Transporter"/> is the transporter's value; the averages and the top performer use the
/// same scope. Gaps are signed so that negative always means behind the comparison, including for claims.
/// </summary>
public sealed record BenchmarkRowDto(
    KpiType Kpi,
    decimal? Transporter,
    decimal? LaneAverage,
    decimal? RegionAverage,
    decimal? CategoryAverage,
    decimal? TopPerformer,
    long? TopPerformerTransporterId,
    decimal? GapToTop,
    decimal? GapToLaneAverage);

public sealed record BenchmarkDto(long TransporterId, string ScopeLabel, DateOnly From, DateOnly To, IReadOnlyList<BenchmarkRowDto> Rows);

public interface IRankingService
{
    Task<RankingResultDto> RankAsync(RankingScope scope, RankingMetric metric, CancellationToken cancellationToken = default);

    Task<BenchmarkDto> BenchmarkAsync(long transporterId, RankingScope scope, CancellationToken cancellationToken = default);
}

/// <summary>
/// Ranking and benchmarking from the stored monthly KPIs, using the same pooled values, minimum sample and weights as
/// scorecards. A transporter below the minimum sample for the ranked metric is listed but not ranked, so a handful of
/// loads cannot push it to the top.
/// </summary>
public sealed class RankingService(
    IRepository<Transporter> transporters,
    IRepository<TransporterLane> lanes,
    IRepository<PerformanceKpi> kpis,
    ITransporterSettings settings) : IRankingService
{
    private static readonly KpiType[] BenchmarkKpis =
    [
        KpiType.OnTimePickup, KpiType.OnTimeDelivery, KpiType.PlacementCompliance, KpiType.TenderAcceptance,
        KpiType.PodCompliance, KpiType.ClaimsRate, KpiType.CostPerformance, KpiType.Availability
    ];

    public async Task<RankingResultDto> RankAsync(RankingScope scope, RankingMetric metric, CancellationToken cancellationToken = default)
    {
        EnsureScope(scope);
        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSampleSize, cancellationToken);
        var weights = await settings.GetAsync<KpiWeightsSetting>(SettingKeys.KpiWeightsDefault, cancellationToken);
        var scoring = await settings.GetAsync<RecommendationScoringSetting>(SettingKeys.RecommendationScoring, cancellationToken);

        var candidateIds = await CandidateIdsAsync(scope, cancellationToken);
        var rows = await ScopedRowsAsync(scope, cancellationToken);
        if (candidateIds is not null)
        {
            rows = rows.Where(r => candidateIds.Contains(r.TransporterId)).ToList();
        }

        var names = await TransportersByIdAsync(rows.Select(r => r.TransporterId).Distinct().ToList(), cancellationToken);
        var entries = new List<(Transporter Transporter, RankedTransporterDto Row, decimal? SortValue)>();

        foreach (var group in rows.GroupBy(r => r.TransporterId))
        {
            if (!names.TryGetValue(group.Key, out var transporter))
            {
                continue;
            }

            var mine = group.ToList();
            var overall = ScorecardMath.Overall(ScorecardMath.Measure(mine, weights, minimumSample, scoring));
            var kpiValues = KpisOf(mine, minimumSample);

            var metricKpi = MetricKpi(metric);
            var value = metricKpi is null ? overall : kpiValues.GetValueOrDefault(metricKpi.Value).Value;
            var note = value is null
                ? (metricKpi is null ? "No measurable KPIs in this period." : $"Sample below the minimum of {minimumSample} for this metric.")
                : null;

            entries.Add((transporter, new RankedTransporterDto(null, transporter.Id, transporter.TransporterCode, transporter.LegalName,
                transporter.State, transporter.Status, value, overall, value is not null, note,
                kpiValues.Select(k => new RankedKpiDto(k.Key, k.Value.Value, k.Value.Numerator, k.Value.Denominator)).ToList()), value));
        }

        var lower = metric == RankingMetric.ClaimsRate;
        var ranked = entries.Where(e => e.SortValue is not null)
            .OrderBy(e => lower ? e.SortValue!.Value : -e.SortValue!.Value).ThenBy(e => e.Transporter.LegalName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var unranked = entries.Where(e => e.SortValue is null).OrderBy(e => e.Transporter.LegalName, StringComparer.OrdinalIgnoreCase).ToList();

        var result = new List<RankedTransporterDto>();
        for (var i = 0; i < ranked.Count; i++)
        {
            result.Add(ranked[i].Row with { Rank = i + 1 });
        }

        result.AddRange(unranked.Select(e => e.Row));
        return new RankingResultDto(metric, DescribeScope(scope), scope.From, scope.To, result);
    }

    public async Task<BenchmarkDto> BenchmarkAsync(long transporterId, RankingScope scope, CancellationToken cancellationToken = default)
    {
        EnsureScope(scope);
        var transporter = await transporters.FindAsync(transporterId, cancellationToken)
            ?? throw new NotFoundException($"Transporter {transporterId} was not found.", "TRANSPORTER_NOT_FOUND");

        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSampleSize, cancellationToken);

        // Peers are measured within the same scope: the same lane, vehicle type, or transporter level.
        var scopeRows = await ScopedRowsAsync(scope, cancellationToken);
        var byTransporter = scopeRows.GroupBy(r => r.TransporterId).ToDictionary(g => g.Key, g => g.ToList());
        var own = byTransporter.GetValueOrDefault(transporterId, []);

        var regionIds = transporter.State is null ? [] : (await transporters.ListAsync(t => t.State == transporter.State, cancellationToken)).Select(t => t.Id).ToHashSet();
        var categoryIds = transporter.TransporterTypeId is null ? [] : (await transporters.ListAsync(t => t.TransporterTypeId == transporter.TransporterTypeId, cancellationToken)).Select(t => t.Id).ToHashSet();
        var regionRows = scopeRows.Where(r => regionIds.Contains(r.TransporterId)).ToList();
        var categoryRows = scopeRows.Where(r => categoryIds.Contains(r.TransporterId)).ToList();

        var laneRows = await LaneAverageRowsAsync(transporterId, scope, cancellationToken);

        var rows = new List<BenchmarkRowDto>();
        foreach (var kpi in BenchmarkKpis)
        {
            var mine = ScorecardMath.Pooled(own, kpi, minimumSample, out _, out _);
            var lane = ScorecardMath.Pooled(laneRows, kpi, minimumSample, out _, out _);
            var region = regionRows.Count == 0 ? null : ScorecardMath.Pooled(regionRows, kpi, minimumSample, out _, out _);
            var category = categoryRows.Count == 0 ? null : ScorecardMath.Pooled(categoryRows, kpi, minimumSample, out _, out _);

            // The top performer is the best transporter in the scope that meets the minimum sample.
            var candidates = byTransporter
                .Select(kv => (Id: kv.Key, Value: ScorecardMath.Pooled(kv.Value, kpi, minimumSample, out _, out _)))
                .Where(c => c.Value is not null)
                .Select(c => (c.Id, Value: c.Value!.Value)).ToList();
            var lower = ScorecardMath.LowerIsBetter(kpi);
            (long Id, decimal Value)? top = candidates.Count == 0 ? null
                : lower ? candidates.MinBy(c => c.Value) : candidates.MaxBy(c => c.Value);

            var direction = lower ? -1m : 1m;
            rows.Add(new BenchmarkRowDto(kpi, mine, lane, region, category,
                top?.Value, top?.Id,
                mine is null || top is null ? null : Math.Round(direction * (mine.Value - top.Value.Value), 2),
                mine is null || lane is null ? null : Math.Round(direction * (mine.Value - lane.Value), 2)));
        }

        return new BenchmarkDto(transporterId, DescribeScope(scope), scope.From, scope.To, rows);
    }

    /// <summary>
    /// Lane-level rows for the lanes that match the requested lane, or else the transporter's own lanes. Lane average
    /// pools every transporter on those lanes. Only lane and transporter-level scopes have one.
    /// </summary>
    private async Task<List<PerformanceKpi>> LaneAverageRowsAsync(long transporterId, RankingScope scope, CancellationToken ct)
    {
        if (scope.VehicleTypeReference is not null)
        {
            return [];
        }

        List<(long Origin, long Destination, string Service)> keys;
        if (scope.OriginLocationReference is not null)
        {
            keys = [(scope.OriginLocationReference.Value, scope.DestinationLocationReference!.Value, Normalise(scope.ServiceType))];
        }
        else
        {
            keys = (await lanes.ListAsync(l => l.TransporterId == transporterId, ct))
                .Select(l => (l.OriginLocationReference, l.DestinationLocationReference, l.ServiceType)).Distinct().ToList();
        }

        if (keys.Count == 0)
        {
            return [];
        }

        var origins = keys.Select(k => k.Origin).ToHashSet();
        var destinations = keys.Select(k => k.Destination).ToHashSet();
        var laneIds = (await lanes.ListAsync(l => origins.Contains(l.OriginLocationReference) && destinations.Contains(l.DestinationLocationReference), ct))
            .Where(l => keys.Contains((l.OriginLocationReference, l.DestinationLocationReference, l.ServiceType)))
            .Select(l => (long?)l.Id).ToList();

        return (await kpis.ListAsync(k => k.LaneReference != null && laneIds.Contains(k.LaneReference) && k.PeriodStart >= scope.From && k.PeriodEnd <= scope.To, ct)).ToList();
    }

    /// <summary>KPI rows for every transporter in the scope, limited to months that lie fully inside the range.</summary>
    private async Task<List<PerformanceKpi>> ScopedRowsAsync(RankingScope scope, CancellationToken ct)
    {
        if (scope.VehicleTypeReference is { } vehicleType)
        {
            return (await kpis.ListAsync(k => k.VehicleTypeReference == vehicleType && k.LaneReference == null
                && k.PeriodStart >= scope.From && k.PeriodEnd <= scope.To, ct)).ToList();
        }

        if (scope.OriginLocationReference is not null)
        {
            var laneIds = (await lanes.ListAsync(l => l.OriginLocationReference == scope.OriginLocationReference
                && l.DestinationLocationReference == scope.DestinationLocationReference
                && (scope.ServiceType == null || l.ServiceType == Normalise(scope.ServiceType)), ct))
                .Select(l => (long?)l.Id).ToList();
            return (await kpis.ListAsync(k => k.LaneReference != null && laneIds.Contains(k.LaneReference)
                && k.PeriodStart >= scope.From && k.PeriodEnd <= scope.To, ct)).ToList();
        }

        return (await kpis.ListAsync(k => k.LaneReference == null && k.VehicleTypeReference == null
            && k.PeriodStart >= scope.From && k.PeriodEnd <= scope.To, ct)).ToList();
    }

    /// <summary>The transporters that match the region and category filters. Null when neither is set.</summary>
    private async Task<HashSet<long>?> CandidateIdsAsync(RankingScope scope, CancellationToken ct)
    {
        if (scope.Region is null && scope.TransporterTypeId is null)
        {
            return null;
        }

        var region = scope.Region?.Trim();
        var type = scope.TransporterTypeId;
        return (await transporters.ListAsync(t => (region == null || t.State == region) && (type == null || t.TransporterTypeId == type), ct))
            .Select(t => t.Id).ToHashSet();
    }

    private async Task<Dictionary<long, Transporter>> TransportersByIdAsync(List<long> ids, CancellationToken ct) =>
        ids.Count == 0 ? [] : (await transporters.ListAsync(t => ids.Contains(t.Id), ct)).ToDictionary(t => t.Id);

    private static Dictionary<KpiType, (decimal? Value, decimal Numerator, decimal Denominator)> KpisOf(IEnumerable<PerformanceKpi> rows, int minimumSample)
    {
        var list = rows.ToList();
        var result = new Dictionary<KpiType, (decimal?, decimal, decimal)>();
        foreach (var kpi in BenchmarkKpis)
        {
            var value = ScorecardMath.Pooled(list, kpi, minimumSample, out var numerator, out var denominator);
            result[kpi] = (value, numerator, denominator);
        }

        return result;
    }

    private static KpiType? MetricKpi(RankingMetric metric) => metric switch
    {
        RankingMetric.OverallScore => null,
        RankingMetric.OnTimePickup => KpiType.OnTimePickup,
        RankingMetric.OnTimeDelivery => KpiType.OnTimeDelivery,
        RankingMetric.PlacementCompliance => KpiType.PlacementCompliance,
        RankingMetric.TenderAcceptance => KpiType.TenderAcceptance,
        RankingMetric.PodCompliance => KpiType.PodCompliance,
        RankingMetric.ClaimsRate => KpiType.ClaimsRate,
        RankingMetric.CostPerformance => KpiType.CostPerformance,
        RankingMetric.Availability => KpiType.Availability,
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null)
    };

    private static void EnsureScope(RankingScope scope)
    {
        if (scope.From == default || scope.To == default)
        {
            throw new BusinessRuleException("A ranking needs a from and a to date.", "PERIOD_REQUIRED");
        }

        if (scope.To < scope.From)
        {
            throw new BusinessRuleException("The end of the period must be on or after its start.", "PERIOD_INVALID");
        }

        var hasLane = scope.OriginLocationReference is not null || scope.DestinationLocationReference is not null;
        if (hasLane && (scope.OriginLocationReference is null || scope.DestinationLocationReference is null))
        {
            throw new BusinessRuleException("A lane needs both an origin and a destination.", "RANKING_SCOPE_INVALID");
        }

        if (scope.ServiceType is not null && !hasLane)
        {
            throw new BusinessRuleException("A service type applies to a lane. Add an origin and a destination.", "RANKING_SCOPE_INVALID");
        }

        if (hasLane && scope.VehicleTypeReference is not null)
        {
            throw new BusinessRuleException("Choose either a lane or a vehicle type, not both.", "RANKING_SCOPE_INVALID");
        }
    }

    private static string Normalise(string? serviceType) => serviceType?.Trim().ToUpperInvariant() ?? string.Empty;

    private static string DescribeScope(RankingScope scope)
    {
        if (scope.OriginLocationReference is not null)
        {
            return $"Lane {scope.OriginLocationReference}→{scope.DestinationLocationReference}" + (scope.ServiceType is null ? string.Empty : $" / {Normalise(scope.ServiceType)}");
        }

        if (scope.VehicleTypeReference is not null)
        {
            return $"Vehicle type {scope.VehicleTypeReference}";
        }

        return "All lanes";
    }
}
