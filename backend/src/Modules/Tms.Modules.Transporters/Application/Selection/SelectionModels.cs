using FluentValidation;
using Tms.Modules.Transporters.Application.Settings;
using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Contracts;

namespace Tms.Modules.Transporters.Application.Selection;

/// <summary>A load that needs a carrier. Eligibility and recommendation answer "who can take it, and who should".</summary>
public sealed record SelectionRequest(
    string OriginState,
    string? OriginCity,
    string DestinationState,
    string? DestinationCity,
    FreightMode Mode,
    Guid? VehicleTypeId,
    decimal WeightKg,
    decimal? VolumeCbm,
    DateOnly Date,
    IReadOnlyList<string>? RequiredCapabilities = null,
    bool IsUrgent = false,
    decimal? DistanceKm = null,
    DateTimeOffset? PickupBy = null,
    DateTimeOffset? DeliverBy = null);

/// <summary>A KPI as used for scoring. <see cref="Value"/> is null unless the sample is sufficient.</summary>
public sealed record KpiPoint(decimal? Value, decimal Denominator, bool Sufficient, string Scope);

public sealed record RateQuote(Guid ContractId, string ContractReference, decimal Total, string? Note);

/// <summary>The outcome for one transporter. Reasons are always populated for an ineligible transporter, so an exclusion is always explainable.</summary>
public sealed record CandidateEvaluation(
    Guid TransporterId,
    string Code,
    string Name,
    TransporterStatus Status,
    bool Eligible,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<string> Warnings,
    Guid? LaneId,
    bool Preferred,
    bool RestrictedForPlanning,
    RateQuote? Rate,
    int AvailableVehicles,
    IReadOnlyDictionary<KpiType, KpiPoint> Kpis);

/// <summary>Everything the rules need to judge one transporter, loaded in bulk by the caller.</summary>
public sealed record CandidateData(
    Transporter Transporter,
    IReadOnlyList<TransporterLane> Lanes,
    IReadOnlyList<TransporterCapability> Capabilities,
    IReadOnlyList<PlanningRule> Rules,
    IReadOnlyList<DocumentIssue> DocumentIssues,
    IReadOnlyList<FleetVehicle> Vehicles,
    IReadOnlyList<PerformanceKpi> Kpis,
    RateQuote? Rate);

public sealed record DocumentIssue(string Message, bool Blocks);

public sealed record EligibilityParameters(int PerformanceWindowDays, int MinimumSample, EligibilityRestrictionsSetting Restrictions);

/// <summary>One weighted factor in a recommendation. A factor with too little data is scored neutrally and says so.</summary>
public sealed record ScoreComponent(string Factor, decimal Score, decimal Weight, decimal Contribution, string Basis, bool Sufficient);

public sealed record RankedCandidate(
    int Rank, CandidateEvaluation Candidate, decimal RecommendationScore, IReadOnlyList<ScoreComponent> Components, IReadOnlyList<string> Explanations, IReadOnlyList<string> Comparisons);

public sealed record RecommendationResult(RankedCandidate? Recommended, IReadOnlyList<RankedCandidate> Ranked, IReadOnlyList<CandidateEvaluation> Candidates);

internal sealed class SelectionRequestValidator : AbstractValidator<SelectionRequest>
{
    public SelectionRequestValidator()
    {
        RuleFor(x => x.OriginState).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DestinationState).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Mode).IsInEnum();
        RuleFor(x => x.WeightKg).GreaterThan(0).WithMessage("Enter the weight of the load.");
        RuleFor(x => x.VolumeCbm).GreaterThan(0).When(x => x.VolumeCbm.HasValue);
        RuleFor(x => x.DistanceKm).GreaterThan(0).When(x => x.DistanceKm.HasValue);
        RuleFor(x => x.Date).NotEmpty();
        RuleFor(x => x.VehicleTypeId).NotNull().When(x => x.Mode == FreightMode.Ftl).WithMessage("Choose the vehicle type for a full-truck load.");
        RuleFor(x => x.DeliverBy).GreaterThan(x => x.PickupBy!.Value).When(x => x.PickupBy.HasValue && x.DeliverBy.HasValue).WithMessage("Delivery must be after pickup.");
        RuleForEach(x => x.RequiredCapabilities).Must(c => CapabilityCatalog.IsKnown(c)).WithMessage("Unknown capability '{PropertyValue}'.");
    }
}
