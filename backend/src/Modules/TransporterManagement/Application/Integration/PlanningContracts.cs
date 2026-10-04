namespace LogiVue.Tms.TransporterManagement.Application.Integration;

/// <summary>
/// Stable contract consumed by Planning & Load Optimisation (and exposed by the API at
/// <c>/api/v1/transporters/planning/eligible</c>). Planning never references this module's internals.
/// </summary>
public record TransporterSelectionRequest(
    long OriginLocationId,
    long DestinationLocationId,
    long? VehicleTypeId,
    string ServiceType,
    decimal RequiredWeightKg,
    decimal? RequiredVolumeM3,
    DateOnly RequiredDate,
    IReadOnlyList<string> RequiredCapabilities,
    bool IsUrgent = false,
    decimal? DistanceKm = null,
    DateTime? PickupBy = null,
    DateTime? DeliverBy = null);

public record TransporterPlanningProfileDto(
    long TransporterId,
    string Code,
    string Name,
    bool Eligible,
    bool Availability,
    decimal? Rate,
    decimal? OverallScore,
    decimal? OtpPct,
    decimal? OtdPct,
    decimal? PlacementCompliancePct,
    decimal? PodCompliancePct,
    decimal? TenderAcceptancePct,
    decimal? ClaimsRatePct,
    decimal? RecommendationScore,
    int? Rank,
    bool Preferred,
    bool Restricted,
    IReadOnlyList<string> Reasons);

/// <summary>Planning-facing eligibility and recommendation source. Implemented locally now; swapped for the merged Planning module later.</summary>
public interface ITransporterPlanningIntegration
{
    Task<IReadOnlyList<TransporterPlanningProfileDto>> GetEligibleTransportersAsync(
        TransporterSelectionRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Claims metrics owned by the Claims module. Transporter Management only consumes the summary.</summary>
public interface ITransporterClaimsProvider
{
    Task<TransporterClaimsSummaryDto> GetClaimsSummaryAsync(
        long transporterId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}

public record TransporterClaimsSummaryDto(
    long TransporterId,
    int TotalShipments,
    int ClaimsCount,
    decimal ClaimsRatePct,
    decimal ClaimValue,
    int DamageCount,
    int ShortageCount,
    int LossTheftCount,
    int OpenClaims,
    int ResolvedClaims);

/// <summary>POD outcomes owned by the POD module. Transporter Management reads summaries only.</summary>
public interface ITransporterPodProvider
{
    Task<TransporterPodSummaryDto> GetPodSummaryAsync(
        long transporterId,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// POD outcomes for deliveries in a period. <see cref="DeliveriesRequiringPod"/> covers deliveries that were submitted
/// or whose SLA has passed; deliveries still inside their SLA are excluded. <see cref="Rejected"/> counts PODs
/// rejected at least once, and <see cref="Reviewed"/> counts PODs with a final or rejected review outcome.
/// </summary>
public record TransporterPodSummaryDto(
    long TransporterId,
    int DeliveriesRequiringPod,
    int SubmittedWithinSla,
    int Accepted,
    int Rejected,
    int Reviewed,
    int Pending,
    double? AverageSubmissionHours);

/// <summary>Commercial rates owned by the Freight Rate / Procurement module.</summary>
public interface ITransporterRateProvider
{
    Task<decimal?> GetApplicableRateAsync(
        long transporterId,
        long originLocationId,
        long destinationLocationId,
        long? vehicleTypeId,
        string serviceType,
        DateOnly onDate,
        CancellationToken cancellationToken = default);
}

/// <summary>Execution events (pickup, arrival, delivery) produced by Tracking / Execution.</summary>
public interface ITransporterExecutionProvider
{
    Task<IReadOnlyList<ExecutionEventDto>> GetEventsAsync(
        string loadReference,
        CancellationToken cancellationToken = default);
}

public record ExecutionEventDto(
    string LoadReference,
    long TransporterId,
    string EventType,
    DateTime EventAt,
    DateTime? PlannedAt,
    string? DelayAttribution);
