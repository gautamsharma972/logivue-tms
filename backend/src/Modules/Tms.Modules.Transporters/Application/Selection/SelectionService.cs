using Microsoft.EntityFrameworkCore;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;
using Tms.Modules.Transporters.Infrastructure.Persistence;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Transporters.Application.Selection;

/// <summary>
/// Works out which transporters can take a load and which one to recommend. Price comes only from the contract engine, vehicles only from the fleet
/// directory (papers and availability by day), performance from the stored KPIs. Nothing is saved.
/// </summary>
internal sealed class SelectionService(
    TransportersDbContext db, IFleetDirectory fleet, IFreightQuoteService quotes, ITransporterSettings settings, TimeProvider clock)
{
    private const int ExpiryWarningDays = 30;

    public async Task<IReadOnlyList<CandidateEvaluation>> CheckAsync(SelectionRequest request, CancellationToken cancellationToken)
    {
        var parameters = new EligibilityParameters(
            await settings.GetAsync<int>(SettingKeys.PerformanceWindowDays, cancellationToken),
            await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSample, cancellationToken),
            await settings.GetAsync<EligibilityRestrictionsSetting>(SettingKeys.EligibilityRestrictions, cancellationToken));

        var transporters = await db.Transporters.AsNoTracking().ToListAsync(cancellationToken);
        var ids = transporters.Select(t => t.Id).ToList();
        var lanes = (await db.Lanes.AsNoTracking().Where(l => ids.Contains(l.TransporterId)).ToListAsync(cancellationToken)).ToLookup(l => l.TransporterId);
        var capabilities = (await db.Capabilities.AsNoTracking().Where(c => ids.Contains(c.TransporterId)).ToListAsync(cancellationToken)).ToLookup(c => c.TransporterId);
        var rules = (await db.PlanningRules.AsNoTracking().Where(r => ids.Contains(r.TransporterId)).ToListAsync(cancellationToken)).ToLookup(r => r.TransporterId);
        var windowStart = request.Date.AddDays(-parameters.PerformanceWindowDays - 31);
        var kpis = (await db.Kpis.AsNoTracking().Where(k => ids.Contains(k.TransporterId) && k.PeriodEnd >= windowStart && k.PeriodStart <= request.Date).ToListAsync(cancellationToken)).ToLookup(k => k.TransporterId);
        var issues = await DocumentIssuesAsync(ids, cancellationToken);
        var rates = await RatesAsync(request, cancellationToken);

        var candidates = new List<CandidateData>();
        foreach (var t in transporters)
        {
            var vehicles = t.Status == TransporterStatus.Active ? await fleet.ListVehiclesAsync(t.Id, cancellationToken) : [];
            candidates.Add(new CandidateData(
                t, lanes[t.Id].ToList(), capabilities[t.Id].ToList(), rules[t.Id].ToList(), issues.GetValueOrDefault(t.Id, []), vehicles, kpis[t.Id].ToList(), rates.GetValueOrDefault(t.Id)));
        }

        return EligibilityEvaluator.Evaluate(request, candidates, parameters);
    }

    public async Task<RecommendationResult> RecommendAsync(SelectionRequest request, CancellationToken cancellationToken)
    {
        var candidates = await CheckAsync(request, cancellationToken);
        var weights = await settings.GetAsync<RecommendationWeightsSetting>(SettingKeys.RecommendationWeights, cancellationToken);
        var scoring = await settings.GetAsync<RecommendationScoringSetting>(SettingKeys.RecommendationScoring, cancellationToken);
        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSample, cancellationToken);
        var ranked = RecommendationScorer.Rank(candidates.Where(c => c.Eligible).ToList(), weights, scoring, minimumSample);
        return new RecommendationResult(ranked.Count == 0 ? null : ranked[0], ranked, candidates);
    }

    /// <summary>
    /// The cheapest price each transporter's contracts give for this load. When no vehicle type is asked for, every active type is priced and the cheapest one counts: a priced
    /// shipment must still name its vehicle, but a search should not have to.
    /// </summary>
    private async Task<Dictionary<Guid, RateQuote>> RatesAsync(SelectionRequest request, CancellationToken cancellationToken)
    {
        var types = request.VehicleTypeId is { } named
            ? [named]
            : (await db.VehicleTypes.AsNoTracking().Where(t => t.IsActive).Select(t => (Guid?)t.Id).ToListAsync(cancellationToken)).Append(null).ToList();

        var all = new List<FreightQuoteLine2>();
        foreach (var type in types)
        {
            var set = await quotes.QuoteAsync(
                new FreightQuoteRequest(request.Date, request.OriginState, request.OriginCity, request.DestinationState, request.DestinationCity, type, request.Mode,
                    request.WeightKg, request.VolumeCbm, request.DistanceKm), cancellationToken);
            all.AddRange(set.Quotes.Select(q => new FreightQuoteLine2(q.TransporterId, q.ContractId, q.ContractReference, q.Total)));
        }

        return all.GroupBy(q => q.TransporterId).ToDictionary(
            g => g.Key, g => g.OrderBy(q => q.Total).Select(q => new RateQuote(q.ContractId, q.ContractReference, q.Total, null)).First());
    }

    private sealed record FreightQuoteLine2(Guid TransporterId, Guid ContractId, string ContractReference, decimal Total);

    /// <summary>Expired transporter-level documents bar a transporter; ones expiring soon only warn.</summary>
    private async Task<Dictionary<Guid, IReadOnlyList<DocumentIssue>>> DocumentIssuesAsync(List<Guid> ids, CancellationToken cancellationToken)
    {
        var today = clock.TodayInIndia();
        var docs = await db.Documents.AsNoTracking()
            .Where(d => d.OwnerKind == OwnerKind.Transporter && ids.Contains(d.OwnerId) && d.SupersededAt == null && d.ExpiresOn != null)
            .ToListAsync(cancellationToken);
        return docs.GroupBy(d => d.OwnerId).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<DocumentIssue>)g.Where(d => d.ExpiresOn <= today.AddDays(ExpiryWarningDays)).Select(d => d.ExpiresOn < today
                ? new DocumentIssue($"{d.Kind} expired on {d.ExpiresOn:dd MMM yyyy}.", true)
                : new DocumentIssue($"{d.Kind} expires on {d.ExpiresOn:dd MMM yyyy}.", false)).ToList());
    }
}

/// <summary>The planning-facing view of the same rules: may this transporter be given a load here, and is it a favourite.</summary>
internal sealed class TransporterPlanningPolicy(TransportersDbContext db, ITransporterSettings settings, TimeProvider clock) : ITransporterPlanningPolicy
{
    public async Task<IReadOnlyDictionary<Guid, TransporterStanding>> GetStandingsAsync(
        IReadOnlyCollection<Guid> transporterIds, DateOnly date, string originState, string? originCity, string? destinationState, string? destinationCity, FreightMode mode, bool urgent,
        CancellationToken cancellationToken = default)
    {
        var ids = transporterIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, TransporterStanding>();
        }

        var restrictions = await settings.GetAsync<EligibilityRestrictionsSetting>(SettingKeys.EligibilityRestrictions, cancellationToken);
        var window = await settings.GetAsync<int>(SettingKeys.PerformanceWindowDays, cancellationToken);
        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSample, cancellationToken);
        var transporters = await db.Transporters.AsNoTracking().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, cancellationToken);
        var lanes = (await db.Lanes.AsNoTracking().Where(l => ids.Contains(l.TransporterId)).ToListAsync(cancellationToken)).ToLookup(l => l.TransporterId);
        var rules = (await db.PlanningRules.AsNoTracking().Where(r => ids.Contains(r.TransporterId)).ToListAsync(cancellationToken)).ToLookup(r => r.TransporterId);
        var kpis = restrictions.Enabled
            ? (await db.Kpis.AsNoTracking().Where(k => ids.Contains(k.TransporterId) && k.PeriodEnd >= date.AddDays(-window - 31) && k.PeriodStart <= date).ToListAsync(cancellationToken)).ToLookup(k => k.TransporterId)
            : null;
        var scores = await db.Feedback.AsNoTracking().Where(f => ids.Contains(f.TransporterId)).ToDictionaryAsync(f => f.TransporterId, f => f.OverallScore, cancellationToken);
        var today = clock.TodayInIndia();
        var documents = await db.Documents.AsNoTracking()
            .Where(d => d.OwnerKind == OwnerKind.Transporter && ids.Contains(d.OwnerId) && d.SupersededAt == null && d.ExpiresOn != null && d.ExpiresOn < today)
            .ToListAsync(cancellationToken);
        var expired = documents.GroupBy(d => d.OwnerId).ToDictionary(g => g.Key, g => (IReadOnlyList<DocumentIssue>)g.Select(d => new DocumentIssue($"{d.Kind} expired on {d.ExpiresOn:dd MMM yyyy}.", true)).ToList());

        var request = new SelectionRequest(originState, originCity, destinationState ?? string.Empty, destinationCity, mode, null, 1, null, date, null, urgent);
        var result = new Dictionary<Guid, TransporterStanding>();
        foreach (var id in ids)
        {
            if (!transporters.TryGetValue(id, out var transporter))
            {
                continue;
            }

            var lane = EligibilityEvaluator.FindLane(lanes[id], request);
            var standing = EligibilityEvaluator.Standing(transporter.Status, expired.GetValueOrDefault(id, []), rules[id].ToList(), lane?.Id, urgent, date);
            var reasons = standing.Reasons.ToList();
            if (kpis is not null)
            {
                var points = Enum.GetValues<KpiType>().ToDictionary(t => t, t => EligibilityEvaluator.SelectKpi(kpis[id], t, lane?.Id, date, window, minimumSample));
                reasons.AddRange(EligibilityEvaluator.PerformanceReasons(points, restrictions));
            }

            result[id] = new TransporterStanding(id, reasons.Count == 0, reasons.Count == 0 ? null : string.Join(" ", reasons), standing.Preferred, scores.GetValueOrDefault(id));
        }

        return result;
    }
}
