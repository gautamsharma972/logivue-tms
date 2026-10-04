using FluentValidation;
using LogiVue.Tms.TransporterManagement.Application.Configuration;
using LogiVue.Tms.TransporterManagement.Application.Eligibility;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using Microsoft.Extensions.Logging;

namespace LogiVue.Tms.TransporterManagement.Application.Recommendation;

/// <summary>Ranks the transporters that can take a load, with an explanation for each position.</summary>
public interface ITransporterRecommendationService
{
    Task<RecommendationResult> RecommendAsync(TransporterSelectionRequest request, CancellationToken cancellationToken = default);
}

public sealed class TransporterRecommendationService(
    ITransporterEligibilityService eligibility,
    ITransporterSettings settings,
    IValidator<TransporterSelectionRequest> validator,
    ILogger<TransporterRecommendationService> logger) : ITransporterRecommendationService
{
    public async Task<RecommendationResult> RecommendAsync(TransporterSelectionRequest request, CancellationToken cancellationToken = default)
    {
        await validator.ValidateAndThrowAsync(request, cancellationToken);

        var candidates = await eligibility.CheckAsync(request, cancellationToken);
        var weights = await settings.GetAsync<RecommendationWeightsSetting>(SettingKeys.RecommendationWeights, cancellationToken);
        var scoring = await settings.GetAsync<RecommendationScoringSetting>(SettingKeys.RecommendationScoring, cancellationToken);
        var minimumSample = await settings.GetAsync<int>(SettingKeys.ScorecardMinimumSampleSize, cancellationToken);

        var ranked = RecommendationScorer.Rank(candidates.Where(c => c.Eligible).ToList(), weights, scoring, minimumSample);

        logger.LogInformation("Recommendation for lane {Origin}->{Destination}: {Ranked} ranked, top {Top}",
            request.OriginLocationId, request.DestinationLocationId, ranked.Count, ranked.FirstOrDefault()?.Candidate.Code ?? "none");

        return new RecommendationResult(ranked.FirstOrDefault(), ranked, candidates);
    }
}
