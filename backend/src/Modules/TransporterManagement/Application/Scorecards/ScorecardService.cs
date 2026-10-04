using LogiVue.Tms.Shared.Audit;
using LogiVue.Tms.Shared.Errors;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Application.Common;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Transporters;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Performance;
using LogiVue.Tms.TransporterManagement.Domain.Planning;
using LogiVue.Tms.TransporterManagement.Domain.Transporters;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Scorecards;

public sealed record GenerateScorecardRequest(DateOnly From, DateOnly To);

public sealed record ScorecardKpiDto(KpiType Kpi, decimal? KpiValue, decimal Weight, decimal? WeightedScore, decimal Numerator, decimal Denominator);

/// <param name="OverallScore">Weighted average of the measured KPIs. Null when nothing is measurable.</param>
public sealed record ScorecardDto(
    long Id,
    long TransporterId,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal? OverallScore,
    ScorecardStatus Status,
    DateTime GeneratedAt,
    int CalculationVersion,
    IReadOnlyList<ScorecardKpiDto> Kpis);

public interface IScorecardService
{
    Task<ScorecardDto> GenerateAsync(long transporterId, GenerateScorecardRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScorecardDto>> ListAsync(long transporterId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Scorecards from stored KPIs. Each KPI is pooled over the period (sum of numerators over sum of denominators). A KPI
/// with fewer than the minimum sample is excluded and the remaining weights are renormalised, so missing data
/// neither rewards nor penalises the transporter. The planning feedback read model is refreshed from each scorecard.
/// </summary>
public sealed class ScorecardService(
    IRepository<Transporter> transporters,
    IRepository<PerformanceKpi> kpis,
    IRepository<TransporterScorecard> scorecards,
    IRepository<ScorecardDetail> details,
    IRepository<TransporterPlanningFeedback> feedback,
    ITransporterSettings settings,
    IUnitOfWork unitOfWork,
    IAuditTrail audit,
    TimeProvider clock,
    ILogger<ScorecardService> logger) : IScorecardService
{
    public const string DefaultProfile = "Default";
    public const int CalculationVersion = 1;

    public async Task<ScorecardDto> GenerateAsync(long transporterId, GenerateScorecardRequest request, CancellationToken cancellationToken = default)
    {
        if (request.To < request.From)
        {
            throw new BusinessRuleException("The end of the scorecard period must be on or after its start.", "PERIOD_INVALID");
        }

        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);

        var weights = await settings.GetAsync<KpiWeightsSetting>(SettingKeys.KpiWeightsDefault, cancellationToken);
        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSampleSize, cancellationToken);
        var scoring = await settings.GetAsync<RecommendationScoringSetting>(SettingKeys.RecommendationScoring, cancellationToken);
        var rows = (await kpis.ListAsync(k => k.TransporterId == transporterId && k.LaneReference == null && k.VehicleTypeReference == null, cancellationToken))
            .Where(k => k.PeriodStart >= request.From && k.PeriodEnd <= request.To)
            .ToList();

        var measured = ScorecardMath.Measure(rows, weights, minimumSample, scoring);
        var overall = ScorecardMath.Overall(measured);

        var now = clock.GetUtcNow().UtcDateTime;
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var scorecard = new TransporterScorecard
        {
            TransporterId = transporterId,
            ScorecardProfile = DefaultProfile,
            PeriodStart = request.From,
            PeriodEnd = request.To,
            OverallScore = overall,
            Status = ScorecardStatus.Generated,
            GeneratedAt = now,
            CalculationVersion = CalculationVersion
        };
        scorecards.Add(scorecard);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var m in measured)
        {
            details.Add(new ScorecardDetail
            {
                ScorecardId = scorecard.Id,
                KpiType = m.Kpi,
                KpiValue = m.Value,
                Numerator = m.Numerator,
                Denominator = m.Denominator,
                Weight = m.Weight,
                WeightedScore = m.WeightedScore
            });
        }

        await RefreshFeedbackAsync(transporterId, measured.ToDictionary(m => m.Kpi, m => m.Value), overall, now, cancellationToken);
        await audit.RecordAsync(new AuditEntry("TransporterScorecard", scorecard.Id.ToString(), "ScorecardGenerated",
            NewValueJson: AuditJson.Serialize(new { overall, request.From, request.To })), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Scorecard {ScorecardId} generated for transporter {TransporterId}: {Overall}", scorecard.Id, transporterId, overall);
        return (await ToDtosAsync([scorecard], cancellationToken)).Single();
    }

    public async Task<IReadOnlyList<ScorecardDto>> ListAsync(long transporterId, CancellationToken cancellationToken = default)
    {
        await TransporterGuards.LoadAsync(transporters, transporterId, cancellationToken);
        var rows = (await scorecards.ListAsync(s => s.TransporterId == transporterId, cancellationToken))
            .OrderByDescending(s => s.GeneratedAt).ThenByDescending(s => s.Id).ToList();
        return await ToDtosAsync(rows, cancellationToken);
    }

    /// <summary>Upserts the transporter-level planning feedback row that planning reads.</summary>
    private async Task RefreshFeedbackAsync(long transporterId, IReadOnlyDictionary<KpiType, decimal?> values, decimal? overall, DateTime now, CancellationToken cancellationToken)
    {
        var row = (await feedback.ListAsync(f => f.TransporterId == transporterId && f.LaneReference == null, cancellationToken)).SingleOrDefault();
        if (row is null)
        {
            row = new TransporterPlanningFeedback { TransporterId = transporterId };
            feedback.Add(row);
        }

        row.OverallScore = overall;
        row.OtpPct = values.GetValueOrDefault(KpiType.OnTimePickup);
        row.OtdPct = values.GetValueOrDefault(KpiType.OnTimeDelivery);
        row.PlacementCompliancePct = values.GetValueOrDefault(KpiType.PlacementCompliance);
        row.PodCompliancePct = values.GetValueOrDefault(KpiType.PodCompliance);
        row.TenderAcceptancePct = values.GetValueOrDefault(KpiType.TenderAcceptance);
        row.ClaimsRatePct = values.GetValueOrDefault(KpiType.ClaimsRate);
        row.UpdatedAt = now;
    }

    private async Task<IReadOnlyList<ScorecardDto>> ToDtosAsync(IReadOnlyCollection<TransporterScorecard> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(r => r.Id).ToList();
        var lines = ids.Count == 0 ? new List<ScorecardDetail>() : await details.ListAsync(d => ids.Contains(d.ScorecardId), cancellationToken);
        var byScorecard = lines.GroupBy(d => d.ScorecardId).ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(s => new ScorecardDto(s.Id, s.TransporterId, s.PeriodStart, s.PeriodEnd, s.OverallScore, s.Status,
            s.GeneratedAt, s.CalculationVersion,
            byScorecard.GetValueOrDefault(s.Id, []).Select(d => new ScorecardKpiDto(d.KpiType, d.KpiValue, d.Weight, d.WeightedScore, d.Numerator, d.Denominator)).ToList()))
            .ToList();
    }
}
