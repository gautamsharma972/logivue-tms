using LogiVue.Tms.TransporterManagement.Application.Eligibility;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Recommendation;
using LogiVue.Tms.TransporterManagement.Application.Abstractions;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Planning;

namespace LogiVue.Tms.TransporterManagement.Application.Planning;

/// <summary>
/// Local implementation of the Planning contract, backed by this module's own data. It is replaced by the merged
/// Planning module without changing the contract or the callers.
/// </summary>
public sealed class LocalPlanningIntegration(
    ITransporterRecommendationService recommendations,
    IRepository<TransporterPlanningFeedback> feedback) : ITransporterPlanningIntegration
{
    public async Task<IReadOnlyList<TransporterPlanningProfileDto>> GetEligibleTransportersAsync(
        TransporterSelectionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await recommendations.RecommendAsync(request, cancellationToken);
        var ranks = result.Ranked.ToDictionary(r => r.Candidate.TransporterId);
        var ids = result.Candidates.Select(c => c.TransporterId).ToList();
        var scores = (await feedback.ListAsync(f => f.LaneReference == null && ids.Contains(f.TransporterId), cancellationToken))
            .ToDictionary(f => f.TransporterId, f => f.OverallScore);

        return result.Candidates
            .OrderBy(c => ranks.TryGetValue(c.TransporterId, out var r) ? r.Rank : int.MaxValue)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(c =>
            {
                ranks.TryGetValue(c.TransporterId, out var ranked);
                return new TransporterPlanningProfileDto(
                    TransporterId: c.TransporterId,
                    Code: c.Code,
                    Name: c.Name,
                    Eligible: c.Eligible,
                    Availability: c.AvailableVehicles > 0,
                    Rate: c.Rate?.EstimatedCost,
                    OverallScore: scores.GetValueOrDefault(c.TransporterId),
                    OtpPct: Value(c, KpiType.OnTimePickup),
                    OtdPct: Value(c, KpiType.OnTimeDelivery),
                    PlacementCompliancePct: Value(c, KpiType.PlacementCompliance),
                    PodCompliancePct: Value(c, KpiType.PodCompliance),
                    TenderAcceptancePct: Value(c, KpiType.TenderAcceptance),
                    ClaimsRatePct: Value(c, KpiType.ClaimsRate),
                    RecommendationScore: ranked?.RecommendationScore,
                    Rank: ranked?.Rank,
                    Preferred: c.Preferred,
                    Restricted: c.RestrictedForPlanning,
                    Reasons: c.Eligible && ranked is not null ? ranked.Explanations : c.Reasons);
            })
            .ToList();
    }

    private static decimal? Value(CandidateEvaluation c, KpiType type) =>
        c.Kpis.TryGetValue(type, out var point) && point.Sufficient ? point.Value : null;
}
