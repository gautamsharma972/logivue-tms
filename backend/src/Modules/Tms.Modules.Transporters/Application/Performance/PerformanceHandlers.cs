using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Performance;

internal static class PerformanceMapping
{
    public static KpiDto ToDto(this PerformanceKpi k) => new(k.KpiType, k.Numerator, k.Denominator, k.KpiValue);

    public static ExecutionDto ToDto(this LoadExecution e) => new(
        e.Id, e.ShipmentId, e.ShipmentNumber, e.TransporterId, e.PlannedPickupAt, e.ActualPickupAt, e.PickupDelayMinutes, e.PickupDelayReasonCode, e.PickupAttribution,
        e.PlannedDeliveryAt, e.ActualDeliveryAt, e.DeliveryDelayMinutes, e.DeliveryDelayReasonCode, e.DeliveryAttribution, e.Status,
        e.Events.OrderBy(x => x.EventAt).ThenBy(x => x.RecordedAt).Select(x => new ExecutionEventDto(x.EventType, x.EventAt, x.DelayReasonCode, x.Remarks)).ToList());

    public static LaneDto ToDto(this TransporterLane l) => new(
        l.Id, l.TransporterId, l.OriginState, l.OriginCity, l.DestinationState, l.DestinationCity, l.Mode, l.TransitSlaMinutes, l.EffectiveFrom, l.EffectiveTo, l.IsActive, l.Version);

    public static async Task<bool> TransporterExistsAsync(this TransportersDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.Transporters.AnyAsync(t => t.Id == id, cancellationToken);
}

internal sealed class PerformanceHandler(TransportersDbContext db, PerformanceAccess access, PerformanceEngine engine)
{
    public async Task<Result<PerformanceDto>> GetAsync(Guid transporterId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        if (access.CheckRead(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        if (PerformanceEngine.CheckRange(from, to) is { } bad)
        {
            return bad;
        }

        if (!await db.TransporterExistsAsync(transporterId, cancellationToken))
        {
            return PerformanceAccess.NotFound;
        }

        var live = await engine.OperationsAsync(transporterId, from, to, cancellationToken);
        var stored = await db.Kpis.AsNoTracking()
            .Where(k => k.TransporterId == transporterId && k.LaneId == null && k.VehicleTypeId == null && k.PeriodStart >= PerformanceEngine.StartOfMonth(from) && k.PeriodEnd <= PerformanceEngine.EndOfMonth(PerformanceEngine.StartOfMonth(to)))
            .ToListAsync(cancellationToken);
        var months = stored.GroupBy(k => k.PeriodStart).OrderBy(g => g.Key)
            .Select(g => new MonthDto(g.Key, g.OrderBy(k => k.KpiType).Select(k => k.ToDto()).ToList())).ToList();
        return new PerformanceDto(transporterId, from, to, live.Kpis.Select(k => new KpiDto(k.Type, k.Numerator, k.Denominator, k.Value)).ToList(), live.Metrics, months);
    }

    public async Task<Result<IReadOnlyList<OperationalPeriodResult>>> RecalculateAsync(Guid transporterId, PeriodRequest request, CancellationToken cancellationToken)
    {
        if (access.CheckWrite(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        if (PerformanceEngine.CheckRange(request.From, request.To) is { } bad)
        {
            return bad;
        }

        if (!await db.TransporterExistsAsync(transporterId, cancellationToken))
        {
            return PerformanceAccess.NotFound;
        }

        return Result.Success(await engine.RecalculateAsync(transporterId, request.From, request.To, cancellationToken));
    }

    /// <summary>Rebuilds every active transporter, for a month-end run or after correcting history.</summary>
    public async Task<Result<int>> RecalculateAllAsync(PeriodRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return PerformanceAccess.Forbidden;
        }

        if (PerformanceEngine.CheckRange(request.From, request.To) is { } bad)
        {
            return bad;
        }

        var ids = await db.Transporters.AsNoTracking().Select(t => t.Id).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            await engine.RecalculateAsync(id, request.From, request.To, cancellationToken);
        }

        return ids.Count;
    }
}

internal sealed class ScorecardHandler(TransportersDbContext db, PerformanceAccess access, ITransporterSettings settings, ICurrentUser user, TimeProvider clock)
{
    public const int CalculationVersion = 1;

    public async Task<Result<ScorecardDto>> GenerateAsync(Guid transporterId, PeriodRequest request, CancellationToken cancellationToken)
    {
        if (access.CheckWrite(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        if (request.To < request.From)
        {
            return Error.Validation("performance.period_invalid", "The end of the scorecard period must be on or after its start.");
        }

        var transporter = await db.Transporters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transporterId, cancellationToken);
        if (transporter is null || user.TenantId is not { } tenantId)
        {
            return PerformanceAccess.NotFound;
        }

        var weights = await settings.GetAsync<KpiWeightsSetting>(SettingKeys.KpiWeights, cancellationToken);
        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSample, cancellationToken);
        var scoring = await settings.GetAsync<RecommendationScoringSetting>(SettingKeys.RecommendationScoring, cancellationToken);
        var rows = await db.Kpis.AsNoTracking()
            .Where(k => k.TransporterId == transporterId && k.LaneId == null && k.VehicleTypeId == null && k.PeriodStart >= request.From && k.PeriodEnd <= request.To)
            .ToListAsync(cancellationToken);

        var measured = ScorecardMath.Measure(rows, weights, minimumSample, scoring);
        var overall = ScorecardMath.Overall(measured);
        var card = Scorecard.Create(
            tenantId, transporterId, request.From, request.To, overall,
            measured.Select(m => new ScorecardLine(m.Kpi, m.Value, m.Numerator, m.Denominator, m.Weight, m.WeightedScore)).ToList(), clock.GetUtcNow(), user.UserId, CalculationVersion);
        db.Scorecards.Add(card);

        // Planning reads the latest figures from a small table rather than recomputing them.
        var feedback = await db.Feedback.FirstOrDefaultAsync(f => f.TransporterId == transporterId, cancellationToken);
        if (feedback is null)
        {
            feedback = PlanningFeedback.For(tenantId, transporterId);
            db.Feedback.Add(feedback);
        }

        feedback.Refresh(overall, measured.ToDictionary(m => m.Kpi, m => m.Value), clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return ToDto(card);
    }

    public async Task<Result<IReadOnlyList<ScorecardDto>>> ListAsync(Guid transporterId, CancellationToken cancellationToken)
    {
        if (access.CheckRead(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        if (!await db.TransporterExistsAsync(transporterId, cancellationToken))
        {
            return PerformanceAccess.NotFound;
        }

        var cards = await db.Scorecards.AsNoTracking().Where(s => s.TransporterId == transporterId).OrderByDescending(s => s.GeneratedAt).Take(50).ToListAsync(cancellationToken);
        return cards.Select(ToDto).ToList();
    }

    private static ScorecardDto ToDto(Scorecard s) => new(
        s.Id, s.TransporterId, s.PeriodStart, s.PeriodEnd, s.OverallScore, s.GeneratedAt, s.CalculationVersion,
        s.Lines.Select(l => new ScorecardKpiDto(l.Kpi, l.Value, l.Weight, l.WeightedScore, l.Numerator, l.Denominator)).ToList());
}

/// <summary>
/// Ranking and benchmarking from the stored monthly KPIs, using the same pooled values, minimum sample and weights as scorecards. A transporter
/// below the minimum sample for the ranked metric is listed but not ranked, so a handful of loads cannot push it to the top.
/// </summary>
internal sealed class RankingHandler(TransportersDbContext db, PerformanceAccess access, ITransporterSettings settings)
{
    public async Task<Result<RankingResultDto>> RankAsync(RankingQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanSeeAll)
        {
            return PerformanceAccess.Forbidden;
        }

        if (CheckScope(query.From, query.To, query.LaneId, query.VehicleTypeId) is { } bad)
        {
            return bad;
        }

        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSample, cancellationToken);
        var weights = await settings.GetAsync<KpiWeightsSetting>(SettingKeys.KpiWeights, cancellationToken);
        var scoring = await settings.GetAsync<RecommendationScoringSetting>(SettingKeys.RecommendationScoring, cancellationToken);

        var rows = await ScopedRowsAsync(query.From, query.To, query.LaneId, query.VehicleTypeId, cancellationToken);
        var candidates = db.Transporters.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.Region))
        {
            var region = query.Region.Trim();
            candidates = candidates.Where(t => t.State == region);
        }

        if (query.Mode is { } mode && mode != ServiceModes.None)
        {
            candidates = candidates.Where(t => (t.ServiceModes & mode) == mode);
        }

        var transporters = await candidates.ToDictionaryAsync(t => t.Id, cancellationToken);
        var metricKpi = MetricKpi(query.Metric);
        var entries = new List<(Transporter T, RankedTransporterDto Row, decimal? Sort)>();

        foreach (var group in rows.Where(r => transporters.ContainsKey(r.TransporterId)).GroupBy(r => r.TransporterId))
        {
            var transporter = transporters[group.Key];
            var mine = group.ToList();
            var overall = ScorecardMath.Overall(ScorecardMath.Measure(mine, weights, minimumSample, scoring));
            var values = KpisOf(mine, minimumSample);
            var value = metricKpi is null ? overall : values[metricKpi.Value].Value;
            var note = value is null ? (metricKpi is null ? "No measurable KPIs in this period." : $"Sample below the minimum of {minimumSample} for this metric.") : null;

            entries.Add((transporter, new RankedTransporterDto(
                null, transporter.Id, transporter.Code, transporter.LegalName, transporter.State, transporter.Status, value, overall, value is not null, note,
                values.Select(k => new RankedKpiDto(k.Key, k.Value.Value, k.Value.Numerator, k.Value.Denominator)).ToList()), value));
        }

        var lower = metricKpi is { } k && ScorecardMath.LowerIsBetter(k);
        var ranked = entries.Where(e => e.Sort is not null)
            .OrderBy(e => lower ? e.Sort!.Value : -e.Sort!.Value).ThenBy(e => e.T.LegalName, StringComparer.OrdinalIgnoreCase).ToList();
        var result = new List<RankedTransporterDto>();
        for (var i = 0; i < ranked.Count; i++)
        {
            result.Add(ranked[i].Row with { Rank = i + 1 });
        }

        result.AddRange(entries.Where(e => e.Sort is null).OrderBy(e => e.T.LegalName, StringComparer.OrdinalIgnoreCase).Select(e => e.Row));
        return new RankingResultDto(query.Metric, await DescribeAsync(query.LaneId, query.VehicleTypeId, cancellationToken), query.From, query.To, result);
    }

    public async Task<Result<BenchmarkDto>> BenchmarkAsync(Guid transporterId, BenchmarkQuery query, CancellationToken cancellationToken)
    {
        if (!access.CanSeeAll)
        {
            return PerformanceAccess.Forbidden;
        }

        if (CheckScope(query.From, query.To, query.LaneId, query.VehicleTypeId) is { } bad)
        {
            return bad;
        }

        var transporter = await db.Transporters.AsNoTracking().FirstOrDefaultAsync(t => t.Id == transporterId, cancellationToken);
        if (transporter is null)
        {
            return PerformanceAccess.NotFound;
        }

        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSample, cancellationToken);
        var scopeRows = await ScopedRowsAsync(query.From, query.To, query.LaneId, query.VehicleTypeId, cancellationToken);
        var byTransporter = scopeRows.GroupBy(r => r.TransporterId).ToDictionary(g => g.Key, g => g.ToList());
        var own = byTransporter.GetValueOrDefault(transporterId, []);

        var regionIds = (await db.Transporters.AsNoTracking().Where(t => t.State == transporter.State).Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        var modeIds = transporter.ServiceModes == ServiceModes.None ? []
            : (await db.Transporters.AsNoTracking().Where(t => (t.ServiceModes & transporter.ServiceModes) == transporter.ServiceModes).Select(t => t.Id).ToListAsync(cancellationToken)).ToHashSet();
        var regionRows = scopeRows.Where(r => regionIds.Contains(r.TransporterId)).ToList();
        var modeRows = scopeRows.Where(r => modeIds.Contains(r.TransporterId)).ToList();
        var laneRows = await LaneAverageRowsAsync(transporterId, query, cancellationToken);

        var rows = new List<BenchmarkRowDto>();
        foreach (var kpi in ScorecardMath.Scored)
        {
            var mine = ScorecardMath.Pooled(own, kpi, minimumSample, out _, out _);
            var lane = ScorecardMath.Pooled(laneRows, kpi, minimumSample, out _, out _);
            var region = regionRows.Count == 0 ? null : ScorecardMath.Pooled(regionRows, kpi, minimumSample, out _, out _);
            var mode = modeRows.Count == 0 ? null : ScorecardMath.Pooled(modeRows, kpi, minimumSample, out _, out _);

            var candidates = byTransporter
                .Select(kv => (Id: kv.Key, Value: ScorecardMath.Pooled(kv.Value, kpi, minimumSample, out _, out _)))
                .Where(c => c.Value is not null).Select(c => (c.Id, Value: c.Value!.Value)).ToList();
            var lower = ScorecardMath.LowerIsBetter(kpi);
            (Guid Id, decimal Value)? top = candidates.Count == 0 ? null : lower ? candidates.MinBy(c => c.Value) : candidates.MaxBy(c => c.Value);

            var direction = lower ? -1m : 1m;
            rows.Add(new BenchmarkRowDto(
                kpi, mine, lane, region, mode, top?.Value, top?.Id,
                mine is null || top is null ? null : Math.Round(direction * (mine.Value - top.Value.Value), 2),
                mine is null || lane is null ? null : Math.Round(direction * (mine.Value - lane.Value), 2)));
        }

        return new BenchmarkDto(transporterId, await DescribeAsync(query.LaneId, query.VehicleTypeId, cancellationToken), query.From, query.To, rows);
    }

    /// <summary>The lane average pools every transporter on lanes with the same origin and destination as the requested lane, or as the transporter's own lanes.</summary>
    private async Task<List<PerformanceKpi>> LaneAverageRowsAsync(Guid transporterId, BenchmarkQuery query, CancellationToken cancellationToken)
    {
        if (query.VehicleTypeId is not null)
        {
            return [];
        }

        var reference = query.LaneId is { } id
            ? await db.Lanes.AsNoTracking().Where(l => l.Id == id).ToListAsync(cancellationToken)
            : await db.Lanes.AsNoTracking().Where(l => l.TransporterId == transporterId).ToListAsync(cancellationToken);
        if (reference.Count == 0)
        {
            return [];
        }

        var laneIds = await MatchingLaneIdsAsync(reference, cancellationToken);
        return await db.Kpis.AsNoTracking().Where(k => k.LaneId != null && laneIds.Contains(k.LaneId.Value) && k.PeriodStart >= query.From && k.PeriodEnd <= query.To).ToListAsync(cancellationToken);
    }

    private async Task<List<Guid>> MatchingLaneIdsAsync(IReadOnlyList<TransporterLane> reference, CancellationToken cancellationToken)
    {
        var origins = reference.Select(l => l.OriginState).Distinct().ToList();
        var candidates = await db.Lanes.AsNoTracking().Where(l => origins.Contains(l.OriginState)).ToListAsync(cancellationToken);
        return candidates.Where(c => reference.Any(r => r.OriginState == c.OriginState && r.OriginCity == c.OriginCity && r.DestinationState == c.DestinationState && r.DestinationCity == c.DestinationCity))
            .Select(c => c.Id).ToList();
    }

    private async Task<List<PerformanceKpi>> ScopedRowsAsync(DateOnly from, DateOnly to, Guid? laneId, Guid? vehicleTypeId, CancellationToken cancellationToken)
    {
        if (vehicleTypeId is { } vt)
        {
            return await db.Kpis.AsNoTracking().Where(k => k.VehicleTypeId == vt && k.LaneId == null && k.PeriodStart >= from && k.PeriodEnd <= to).ToListAsync(cancellationToken);
        }

        if (laneId is { } lane)
        {
            var reference = await db.Lanes.AsNoTracking().Where(l => l.Id == lane).ToListAsync(cancellationToken);
            var ids = await MatchingLaneIdsAsync(reference, cancellationToken);
            return await db.Kpis.AsNoTracking().Where(k => k.LaneId != null && ids.Contains(k.LaneId.Value) && k.PeriodStart >= from && k.PeriodEnd <= to).ToListAsync(cancellationToken);
        }

        return await db.Kpis.AsNoTracking().Where(k => k.LaneId == null && k.VehicleTypeId == null && k.PeriodStart >= from && k.PeriodEnd <= to).ToListAsync(cancellationToken);
    }

    private async Task<string> DescribeAsync(Guid? laneId, Guid? vehicleTypeId, CancellationToken cancellationToken)
    {
        if (laneId is { } id && await db.Lanes.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id, cancellationToken) is { } lane)
        {
            return $"Lane {lane.OriginCity ?? lane.OriginState} → {lane.DestinationCity ?? lane.DestinationState}";
        }

        if (vehicleTypeId is { } vt && await db.VehicleTypes.AsNoTracking().FirstOrDefaultAsync(v => v.Id == vt, cancellationToken) is { } type)
        {
            return $"Vehicle type {type.Name}";
        }

        return "All lanes";
    }

    private static Dictionary<KpiType, (decimal? Value, decimal Numerator, decimal Denominator)> KpisOf(IReadOnlyCollection<PerformanceKpi> rows, int minimumSample)
    {
        var result = new Dictionary<KpiType, (decimal?, decimal, decimal)>();
        foreach (var kpi in ScorecardMath.Scored)
        {
            var value = ScorecardMath.Pooled(rows, kpi, minimumSample, out var numerator, out var denominator);
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
        _ => throw new ArgumentOutOfRangeException(nameof(metric), metric, null),
    };

    private static Error? CheckScope(DateOnly from, DateOnly to, Guid? laneId, Guid? vehicleTypeId) =>
        from == default || to == default ? Error.Validation("performance.period_required", "A ranking needs a from and a to date.")
        : to < from ? Error.Validation("performance.period_invalid", "The end of the period must be on or after its start.")
        : laneId is not null && vehicleTypeId is not null ? Error.Validation("performance.scope_invalid", "Choose either a lane or a vehicle type, not both.")
        : null;
}

internal sealed class ExecutionHandler(TransportersDbContext db, PerformanceAccess access, ExecutionService executions, ITransporterSettings settings)
{
    public async Task<Result<IReadOnlyList<ExecutionDto>>> ListAsync(Guid transporterId, CancellationToken cancellationToken)
    {
        if (access.CheckRead(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var list = await db.Executions.AsNoTracking().Include(e => e.Events).Where(e => e.TransporterId == transporterId)
            .OrderByDescending(e => e.PlannedPickupAt).ThenByDescending(e => e.CreatedAt).Take(100).ToListAsync(cancellationToken);
        return list.Select(e => e.ToDto()).ToList();
    }

    public async Task<Result<ExecutionDto>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var execution = await db.Executions.AsNoTracking().Include(e => e.Events).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (execution is null || access.CheckRead(execution.TransporterId).IsFailure)
        {
            return NotFound;
        }

        return execution.ToDto();
    }

    /// <summary>Creates the execution record for an accepted shipment by hand (the system normally does it when the shipment is accepted).</summary>
    public async Task<Result<ExecutionDto>> CreateAsync(CreateExecutionRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return PerformanceAccess.Forbidden;
        }

        var execution = await executions.EnsureAsync(request.ShipmentId, cancellationToken);
        if (execution is null)
        {
            return Error.NotFound("executions.shipment_not_found", "That shipment does not exist or has not been accepted by a transporter.");
        }

        await db.SaveChangesAsync(cancellationToken);
        return execution.ToDto();
    }

    public async Task<Result<ExecutionDto>> RecordAsync(Guid id, RecordExecutionEventRequest request, CancellationToken cancellationToken)
    {
        var execution = await db.Executions.Include(e => e.Events).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (execution is null)
        {
            return NotFound;
        }

        if (access.CheckWrite(execution.TransporterId, vendorMayWrite: true) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var recorded = await executions.RecordAsync(execution, request.EventType, request.EventAt, request.DelayReasonCode, request.Remarks, cancellationToken);
        return recorded.IsFailure ? recorded.Error : execution.ToDto();
    }

    /// <summary>A person says why a late pickup or delivery was late. Moves it between carrier and non-carrier; the minutes never change.</summary>
    public async Task<Result<ExecutionDto>> AttributeAsync(Guid id, AttributeDelayRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage)
        {
            return PerformanceAccess.Forbidden;
        }

        var execution = await db.Executions.Include(e => e.Events).FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
        if (execution is null)
        {
            return NotFound;
        }

        var policy = await settings.GetAsync<DelayPolicySetting>(SettingKeys.ExecutionDelayPolicy, cancellationToken);
        var reason = policy.Find(request.ReasonCode);
        if (reason is null)
        {
            return Error.Validation("executions.delay_reason_invalid", $"'{request.ReasonCode}' is not a valid delay reason.");
        }

        var attributed = execution.Attribute(request.Delivery, reason);
        if (attributed.IsFailure)
        {
            return attributed.Error;
        }

        await db.SaveChangesAsync(cancellationToken);
        await executions.RefreshKpisAsync(execution, cancellationToken);
        return execution.ToDto();
    }

    private static readonly Error NotFound = Error.NotFound("executions.not_found", "Execution not found.");
}

internal sealed class LaneHandler(TransportersDbContext db, PerformanceAccess access, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<LaneDto>>> ListAsync(Guid transporterId, CancellationToken cancellationToken)
    {
        if (access.CheckRead(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        var lanes = await db.Lanes.AsNoTracking().Where(l => l.TransporterId == transporterId).OrderBy(l => l.OriginState).ThenBy(l => l.DestinationState).ToListAsync(cancellationToken);
        return lanes.Select(l => l.ToDto()).ToList();
    }

    public async Task<Result<LaneDto>> CreateAsync(Guid transporterId, SaveLaneRequest request, CancellationToken cancellationToken)
    {
        if (access.CheckWrite(transporterId) is { IsFailure: true } denied)
        {
            return denied.Error;
        }

        if (!await db.TransporterExistsAsync(transporterId, cancellationToken) || user.TenantId is not { } tenantId)
        {
            return PerformanceAccess.NotFound;
        }

        var lane = TransporterLane.Create(
            tenantId, transporterId, request.OriginState, request.OriginCity, request.DestinationState, request.DestinationCity, request.Mode, request.TransitSlaMinutes,
            request.EffectiveFrom, request.EffectiveTo);
        if (lane.IsFailure)
        {
            return lane.Error;
        }

        if (await OverlapAsync(lane.Value, null, cancellationToken) is { } overlap)
        {
            return overlap;
        }

        db.Lanes.Add(lane.Value);
        await db.SaveChangesAsync(cancellationToken);
        return lane.Value.ToDto();
    }

    public async Task<Result<LaneDto>> UpdateAsync(Guid laneId, SaveLaneRequest request, CancellationToken cancellationToken)
    {
        var lane = await db.Lanes.FirstOrDefaultAsync(l => l.Id == laneId, cancellationToken);
        if (lane is null || access.CheckWrite(lane.TransporterId).IsFailure)
        {
            return Error.NotFound("lanes.not_found", "Lane not found.");
        }

        if (request.Version is { } version)
        {
            db.Entry(lane).Property(x => x.Version).OriginalValue = version;
        }

        var set = lane.Set(
            request.OriginState, request.OriginCity, request.DestinationState, request.DestinationCity, request.Mode, request.TransitSlaMinutes,
            request.EffectiveFrom, request.EffectiveTo, request.IsActive);
        if (set.IsFailure)
        {
            return set.Error;
        }

        if (lane.IsActive && await OverlapAsync(lane, lane.Id, cancellationToken) is { } overlap)
        {
            return overlap;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("lanes.concurrent_update", "Someone else changed this lane. Reload and try again.");
        }

        return lane.ToDto();
    }

    /// <summary>A transporter may not have two active lanes for the same route and service whose periods overlap: eligibility would be ambiguous.</summary>
    private async Task<Error?> OverlapAsync(TransporterLane lane, Guid? excludeId, CancellationToken cancellationToken)
    {
        var others = await db.Lanes.AsNoTracking().Where(l => l.TransporterId == lane.TransporterId && l.IsActive && l.Id != (excludeId ?? Guid.Empty)).ToListAsync(cancellationToken);
        return others.Any(o => o.SameRouteAs(lane) && o.OverlapsPeriod(lane.EffectiveFrom, lane.EffectiveTo))
            ? Error.Conflict("lanes.overlap", "An active lane for this route and service already overlaps that period.")
            : null;
    }
}

internal sealed class SettingsHandler(TransportersDbContext db, PerformanceAccess access, ICurrentUser user)
{
    public async Task<Result<IReadOnlyList<SettingDto>>> ListAsync(CancellationToken cancellationToken)
    {
        if (!access.CanSeeAll)
        {
            return PerformanceAccess.Forbidden;
        }

        var stored = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.ValueJson, cancellationToken);
        var result = new List<SettingDto>();
        foreach (var key in SettingDefaults.Keys.Order(StringComparer.Ordinal))
        {
            var isDefault = !stored.TryGetValue(key, out var json);
            var element = isDefault
                ? System.Text.Json.JsonSerializer.SerializeToElement(SettingDefaults.For(key), TransporterSettings.Json)
                : System.Text.Json.JsonDocument.Parse(json!).RootElement.Clone();
            result.Add(new SettingDto(key, element, isDefault));
        }

        return result;
    }

    public async Task<Result<SettingDto>> SaveAsync(string key, SaveSettingRequest request, CancellationToken cancellationToken)
    {
        if (!access.CanManage || user.TenantId is not { } tenantId)
        {
            return PerformanceAccess.Forbidden;
        }

        var type = SettingDefaults.TypeOf(key);
        if (type is null)
        {
            return Error.NotFound("settings.unknown", $"There is no setting called '{key}'.");
        }

        object? parsed;
        try
        {
            parsed = System.Text.Json.JsonSerializer.Deserialize(request.Value, type, TransporterSettings.Json);
        }
        catch (System.Text.Json.JsonException)
        {
            return Error.Validation("settings.invalid", $"The value does not have the shape of '{key}'.");
        }

        if (parsed is null)
        {
            return Error.Validation("settings.invalid", $"The value does not have the shape of '{key}'.");
        }

        var json = System.Text.Json.JsonSerializer.Serialize(parsed, TransporterSettings.Json);
        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (row is null)
        {
            db.Settings.Add(TransporterSetting.Create(tenantId, key, json));
        }
        else
        {
            row.Change(json);
        }

        await db.SaveChangesAsync(cancellationToken);
        return new SettingDto(key, System.Text.Json.JsonDocument.Parse(json).RootElement.Clone(), false);
    }
}
