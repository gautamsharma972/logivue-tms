using LogiVue.Tms.TransporterManagement.Application.Compliance;
using LogiVue.Tms.TransporterManagement.Application.Eligibility;
using LogiVue.Tms.TransporterManagement.Application.Integration;
using LogiVue.Tms.TransporterManagement.Application.Recommendation;
using LogiVue.Tms.TransporterManagement.Domain.Common;
using Microsoft.AspNetCore.Http;
using LogiVue.Tms.TransporterManagement.Api.Filters;
using Microsoft.AspNetCore.Mvc;

namespace LogiVue.Tms.TransporterManagement.Api.Controllers;

public sealed record CandidateDto(
    long TransporterId,
    string Code,
    string Name,
    TransporterStatus Status,
    bool Eligible,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Warnings,
    long? LaneId,
    bool Preferred,
    bool RestrictedForPlanning,
    RateQuote? Rate,
    int AvailableVehicles,
    ComplianceOverallStatus ComplianceStatus);

public sealed record RankedDto(
    int Rank,
    long TransporterId,
    string Code,
    string Name,
    decimal RecommendationScore,
    bool Preferred,
    RateQuote? Rate,
    int AvailableVehicles,
    IReadOnlyList<ScoreComponent> Components,
    IReadOnlyList<string> Explanations,
    IReadOnlyList<string> Comparisons);

public sealed record RecommendationResponse(RankedDto? Recommended, IReadOnlyList<RankedDto> Ranked, IReadOnlyList<CandidateDto> Candidates);

/// <summary>Eligibility and carrier recommendation for a load.</summary>
[ApiController]
[Route("api/v1/transporters")]
[Produces("application/json")]
[InternalUsersOnly]
public sealed class EligibilityController(
    ITransporterEligibilityService eligibility,
    ITransporterRecommendationService recommendations,
    ITransporterPlanningIntegration planning) : ControllerBase
{
    /// <summary>Checks every transporter against the load's hard rules. Ineligible transporters carry their reasons.</summary>
    [HttpPost("eligibility/check")]
    [ProducesResponseType<IReadOnlyList<CandidateDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<CandidateDto>>> Check([FromBody] TransporterSelectionRequest request, CancellationToken cancellationToken) =>
        Ok((await eligibility.CheckAsync(request, cancellationToken)).Select(ToCandidate).ToList());

    /// <summary>Ranks eligible transporters with weighted, explainable scores and comparisons with the recommended carrier.</summary>
    [HttpPost("recommendations")]
    [ProducesResponseType<RecommendationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RecommendationResponse>> Recommend([FromBody] TransporterSelectionRequest request, CancellationToken cancellationToken)
    {
        var result = await recommendations.RecommendAsync(request, cancellationToken);
        return Ok(new RecommendationResponse(
            result.Recommended is null ? null : ToRanked(result.Recommended),
            result.Ranked.Select(ToRanked).ToList(),
            result.Candidates.Select(ToCandidate).ToList()));
    }

    /// <summary>
    /// Planning contract. Takes the load as query parameters so it can be a GET. Returns every candidate with its
    /// eligibility, rank, and reasons.
    /// </summary>
    [HttpGet("planning/eligible")]
    [ProducesResponseType<IReadOnlyList<TransporterPlanningProfileDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TransporterPlanningProfileDto>>> PlanningEligible(
        [FromQuery] long originLocationId,
        [FromQuery] long destinationLocationId,
        [FromQuery] string serviceType,
        [FromQuery] decimal requiredWeightKg,
        [FromQuery] DateOnly requiredDate,
        [FromQuery] long? vehicleTypeId,
        [FromQuery] decimal? requiredVolumeM3,
        [FromQuery] string[]? requiredCapabilities,
        [FromQuery] bool urgent = false,
        [FromQuery] decimal? distanceKm = null,
        [FromQuery] DateTime? pickupBy = null,
        [FromQuery] DateTime? deliverBy = null,
        CancellationToken cancellationToken = default)
    {
        var request = new TransporterSelectionRequest(
            originLocationId, destinationLocationId, vehicleTypeId, serviceType, requiredWeightKg, requiredVolumeM3,
            requiredDate, requiredCapabilities ?? [], urgent, distanceKm, pickupBy, deliverBy);

        return Ok(await planning.GetEligibleTransportersAsync(request, cancellationToken));
    }

    private static CandidateDto ToCandidate(CandidateEvaluation c) =>
        new(c.TransporterId, c.Code, c.Name, c.Status, c.Eligible, c.Reasons, c.Warnings, c.LaneId, c.Preferred,
            c.RestrictedForPlanning, c.Rate, c.AvailableVehicles, c.ComplianceStatus);

    private static RankedDto ToRanked(RankedCandidate r) =>
        new(r.Rank, r.Candidate.TransporterId, r.Candidate.Code, r.Candidate.Name, r.RecommendationScore, r.Candidate.Preferred,
            r.Candidate.Rate, r.Candidate.AvailableVehicles, r.Components, r.Explanations, r.Comparisons);
}
