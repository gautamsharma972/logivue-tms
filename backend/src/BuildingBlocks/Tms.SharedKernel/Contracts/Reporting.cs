namespace Tms.SharedKernel.Contracts;

// Reporting contracts (Module 6). Each transactional module describes what it knows as flat, read-only "facts" keyed by reference ids
// (shipment, trip, transporter, vehicle, customer, contract) and Reports combines them. Nothing here is a copy that can be written back:
// the owning module stays the source of truth for every number, and Reports never recreates a rule another module owns.
// A fact a module cannot supply is null; Reports reads null as "not measurable", never as zero.

/// <summary>What a report asks a provider for. Providers may use <see cref="TransporterId"/> to read less; Reports filters again, so ignoring it is safe.</summary>
public sealed record ReportingWindow(DateOnly From, DateOnly To, Guid? TransporterId = null)
{
    public bool Contains(DateOnly date) => date >= From && date <= To;

    public bool Contains(DateTimeOffset moment) => Contains(DateOnly.FromDateTime(moment.UtcDateTime.AddMinutes(330)));
}

// ---- Planning and shipments (owned by Shipments)

/// <summary>One shipment as planned and as it ran, from the planning and shipment side. Joined with the other facts on <see cref="ShipmentRef"/>.</summary>
/// <param name="Service">FTL, PTL or Dedicated.</param>
/// <param name="PlannedCost">The planner's estimate when the load was planned.</param>
/// <param name="FreightEstimate">The contract-priced freight kept against the shipment (the transporter's contractual price).</param>
/// <param name="ConsolidatedFrom">How many separate orders were put on this trip (1 = not consolidated).</param>
public sealed record ShipmentReportFact(
    string ShipmentRef,
    Guid ShipmentId,
    string Status,
    string Service,
    DateOnly PlannedPickupDate,
    string Customer,
    string OriginCity,
    string OriginState,
    string DestinationCity,
    string DestinationState,
    string Region,
    decimal WeightKg,
    decimal? VolumeCbm,
    decimal? DistanceKm,
    int Orders,
    int Stops,
    Guid? TransporterId,
    string? TransporterName,
    string? VehicleRef,
    string? VehicleType,
    int? VehiclePayloadKg,
    decimal? VehicleVolumeCbm,
    string? DriverName,
    decimal? PlannedCost,
    decimal? FreightEstimate,
    string? ContractRef,
    DateTimeOffset? PlannedPickupAt,
    DateTimeOffset? ActualPickupAt,
    DateOnly? DeliverBy,
    DateTimeOffset? DeliveredAt,
    DateTimeOffset? TenderedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? DispatchedAt,
    int ConsolidatedFrom = 1,
    string? PlanRef = null,
    string? BusinessUnit = null,
    decimal? EmptyKm = null)
{
    public string Lane => $"{OriginCity} → {DestinationCity}";

    public bool IsCancelled => Status.Equals("Cancelled", StringComparison.OrdinalIgnoreCase);
}

/// <param name="Orders">Orders chosen / planned / left out by the run.</param>
public sealed record PlanningRunFact(
    string RunRef,
    Guid RunId,
    DateOnly PlanningDate,
    int Version,
    string Status,
    int OrdersSelected,
    int OrdersPlanned,
    int OrdersUnplanned,
    int VehiclesUsed,
    decimal TotalDistanceKm,
    decimal EstimatedCost,
    decimal? AvgWeightUtilisationPct,
    decimal? AvgVolumeUtilisationPct,
    decimal? CostBeforeConsolidation,
    decimal? CostAfterConsolidation,
    decimal? PlanningSavings);

/// <summary>One vehicle (trip) of a planning run.</summary>
public sealed record PlanVehicleFact(
    string RunRef,
    DateOnly PlanningDate,
    string TripRef,
    string? ShipmentRef,
    string? VehicleRef,
    string VehicleType,
    Guid? TransporterId,
    string? TransporterName,
    string Service,
    string Lane,
    string Region,
    int Orders,
    int Stops,
    decimal WeightKg,
    decimal VolumeCbm,
    decimal? WeightUtilisationPct,
    decimal? VolumeUtilisationPct,
    decimal EstimatedCost,
    decimal DistanceKm,
    decimal? LoadedKm,
    decimal? EmptyKm,
    bool Consolidated,
    decimal? CostIfSeparate,
    decimal? ExtraKmFromConsolidation,
    int? ExtraStopsFromConsolidation,
    int? CapacityKg = null,
    decimal? CapacityCbm = null);

/// <param name="ReasonCategory">NoVehicle, PayloadExceeded, VolumeExceeded, SlaImpossible, NoCompatibleVehicle, NoRate, NoRoute, LockedConflict, Other.</param>
public sealed record UnplannedOrderFact(
    string RunRef,
    DateOnly PlanningDate,
    string OrderRef,
    string Customer,
    string OriginCity,
    string DestinationCity,
    decimal WeightKg,
    decimal? VolumeCbm,
    string ReasonCategory,
    string Reason,
    string? SuggestedAction);

/// <param name="Outcome">Accepted, Rejected, Expired, Withdrawn or Pending.</param>
public sealed record TenderFact(
    string ShipmentRef,
    Guid TransporterId,
    string TransporterName,
    string Lane,
    string? VehicleType,
    string Service,
    DateTimeOffset OfferedAt,
    DateTimeOffset? ViewedAt,
    DateTimeOffset? RespondedAt,
    string Outcome);

public interface IPlanningReportingProvider
{
    Task<IReadOnlyList<ShipmentReportFact>> ShipmentsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlanningRunFact>> RunsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlanVehicleFact>> VehiclesAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnplannedOrderFact>> UnplannedAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenderFact>> TendersAsync(ReportingWindow window, CancellationToken cancellationToken = default);
}

// ---- Transporters (owned by Transporters)

public sealed record TransporterFact(Guid Id, string Code, string Name, string Region, bool IsActive, int Vehicles, int Drivers);

/// <summary>A score the Transporters module calculated for a period. Reports shows it; it never recalculates it. Null = not measurable.</summary>
public sealed record ScorecardFact(
    Guid TransporterId,
    string TransporterName,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string? Lane,
    decimal? OverallScore,
    decimal? Otp,
    decimal? Otd,
    decimal? Placement,
    decimal? TenderAcceptance,
    decimal? PodCompliance,
    decimal? ClaimsRate,
    decimal? CostPerformance,
    decimal? Availability,
    string CalculationVersion);

/// <param name="Outcome">OnTime, Late, NoShow or Replaced.</param>
public sealed record PlacementFact(
    string PlacementRef,
    string? ShipmentRef,
    Guid TransporterId,
    string TransporterName,
    string Lane,
    string? VehicleType,
    string Service,
    DateTimeOffset RequestedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? ReportedAt,
    DateTimeOffset? PlacedAt,
    DateTimeOffset RequiredBy,
    string Outcome,
    int? DelayMinutes);

/// <summary>Planned and actual pickup and delivery times of a load, with who the delay was judged to belong to, as Transporter Management holds them. Fills what Planning cannot know.</summary>
/// <param name="PickupResponsibility">Carrier, NonCarrier or null when nobody has judged a late pickup.</param>
public sealed record ExecutionFact(
    string ShipmentRef,
    Guid TransporterId,
    DateTimeOffset? PlannedPickupAt,
    DateTimeOffset? ActualPickupAt,
    string? PickupResponsibility,
    DateTimeOffset? PlannedDeliveryAt,
    DateTimeOffset? ActualDeliveryAt,
    string? DeliveryResponsibility);

public interface ITransporterReportingProvider
{
    Task<IReadOnlyList<ExecutionFact>> ExecutionsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TransporterFact>> TransportersAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ScorecardFact>> ScorecardsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlacementFact>> PlacementsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExceptionFact>> ExceptionsAsync(ReportingWindow window, CancellationToken cancellationToken = default);
}

// ---- Delivery and proof of delivery (owned by Deliveries)

/// <param name="Status">Delivered, PartiallyDelivered, Failed, Refused, InTransit or Pending.</param>
/// <param name="PromisedBy">When it had to arrive; null = no planned time, so on-time is not measurable.</param>
/// <param name="OnTime">The Deliveries module's own judgement; null when it could not judge.</param>
/// <param name="DelayResponsibility">Carrier or NonCarrier when a late delivery has been judged; null when nobody has judged it. Blame is a finding, never a default.</param>
public sealed record DeliveryFact(
    string DeliveryRef,
    string? ShipmentRef,
    Guid? TransporterId,
    string? TransporterName,
    string Customer,
    string OriginCity,
    string DestinationCity,
    DateTimeOffset? PromisedBy,
    DateTimeOffset? CompletedAt,
    string Status,
    bool? OnTime,
    int Attempts,
    bool PodRequired,
    string? FailureReason,
    string? Location,
    string? NextAction,
    DateOnly Date,
    string? DelayResponsibility = null,
    string? DelayReason = null)
{
    public string Lane => $"{OriginCity} → {DestinationCity}";
}

/// <param name="Status">Pending, Submitted, Accepted, Rejected or ResubmissionRequired.</param>
public sealed record PodFact(
    string PodRef,
    string DeliveryRef,
    string? ShipmentRef,
    Guid? TransporterId,
    string? TransporterName,
    string Customer,
    bool Required,
    DateTimeOffset? DeliveryCompletedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ReviewedAt,
    string Status,
    DateTimeOffset? RejectedAt,
    string? RejectedBy,
    string? RejectionReason,
    DateTimeOffset? ResubmittedAt,
    bool? SubmittedWithinSla,
    DateOnly Date);

/// <param name="Type">Shortage or Damage.</param>
public sealed record DiscrepancyFact(
    string DeliveryRef,
    string? ShipmentRef,
    string Customer,
    Guid? TransporterId,
    string? TransporterName,
    string Sku,
    decimal Ordered,
    decimal Dispatched,
    decimal Delivered,
    decimal Quantity,
    string Type,
    string? Reason,
    string? DamageType,
    bool HasEvidence,
    string? ClaimRef,
    decimal? Value,
    DateOnly Date);

public interface IPodReportingProvider
{
    Task<IReadOnlyList<DeliveryFact>> DeliveriesAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PodFact>> PodsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DiscrepancyFact>> DiscrepanciesAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExceptionFact>> ExceptionsAsync(ReportingWindow window, CancellationToken cancellationToken = default);
}

// ---- Tracking (owned by Tracking)

/// <param name="Health">Healthy, Stale, Lost, NotStarted or Completed.</param>
/// <param name="Risk">OnTime, AtRisk, Delayed or SeverelyDelayed.</param>
/// <param name="Execution">NotStarted, InTransit, Completed or Cancelled.</param>
public sealed record TrackFact(
    string TripRef,
    string? ShipmentRef,
    string? VehicleRef,
    Guid? TransporterId,
    string? TransporterName,
    string Lane,
    string Health,
    string Execution,
    string Risk,
    DateTimeOffset? PlannedEta,
    DateTimeOffset? LatestEta,
    DateTimeOffset? ActualArrival,
    decimal? PlannedDistanceKm,
    decimal? ActualDistanceKm,
    int? PlannedDurationMin,
    int? ActualDurationMin,
    int? PlannedStops,
    int? ActualStops,
    int UnplannedStops,
    int DwellMinutes,
    decimal DeviationKm,
    double? LastLatitude,
    double? LastLongitude,
    DateTimeOffset? LastSeenAt,
    int OpenExceptions,
    DateOnly Date,
    IReadOnlyList<TrackPoint>? PlannedRoute = null,
    IReadOnlyList<TrackPoint>? ActualRoute = null);

public sealed record TrackPoint(double Latitude, double Longitude);

public sealed record DeviationFact(
    string TripRef,
    string? ShipmentRef,
    string? VehicleRef,
    Guid? TransporterId,
    string? TransporterName,
    string Route,
    decimal DeviationKm,
    int DurationMin,
    DateTimeOffset DetectedAt,
    string? Reason,
    string Status);

/// <param name="Kind">Origin, Customer, Hub, Unplanned.</param>
public sealed record DwellFact(
    string TripRef,
    string? ShipmentRef,
    Guid? TransporterId,
    string Kind,
    string Place,
    int ExpectedMinutes,
    int ActualMinutes,
    DateTimeOffset At);

public sealed record GapFact(
    string TripRef,
    string? ShipmentRef,
    string? VehicleRef,
    Guid? TransporterId,
    string? TransporterName,
    string? LastKnownPlace,
    DateTimeOffset GapStart,
    DateTimeOffset? GapEnd,
    int DurationMin,
    string Severity,
    string Status);

public interface ITrackingReportingProvider
{
    Task<IReadOnlyList<TrackFact>> TripsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeviationFact>> DeviationsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DwellFact>> DwellsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<GapFact>> GapsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ExceptionFact>> ExceptionsAsync(ReportingWindow window, CancellationToken cancellationToken = default);
}

// ---- Freight contracts (owned by Contracts)

/// <param name="RenewalStatus">None, Due, Renewed or Lapsed.</param>
public sealed record ContractFact(
    string ContractRef,
    Guid ContractId,
    int Version,
    Guid TransporterId,
    string TransporterName,
    string Type,
    DateOnly Start,
    DateOnly End,
    string Status,
    string RenewalStatus,
    int Rates);

public sealed record RateFact(
    string ContractRef,
    int ContractVersion,
    Guid TransporterId,
    string TransporterName,
    string RateCode,
    int RateVersion,
    string Origin,
    string Destination,
    string? Zone,
    string? VehicleType,
    string Service,
    string? WeightSlab,
    string? DistanceSlab,
    string? VolumeSlab,
    decimal Rate,
    string RateBasis,
    decimal? Minimum,
    decimal? Maximum,
    DateOnly? ValidFrom,
    DateOnly? ValidTo);

public sealed record DphFact(
    string ContractRef,
    Guid TransporterId,
    string TransporterName,
    decimal BaseDieselPrice,
    decimal CurrentDieselPrice,
    decimal VariationPct,
    decimal FuelComponentPct,
    decimal AdjustmentPct,
    DateOnly EffectiveDate,
    string Accessorials);

/// <summary>A kept freight rating with everything needed to reproduce it. Historical: values are as they were when the rating was made.</summary>
public sealed record RatingFact(
    string ShipmentRef,
    Guid TransporterId,
    string TransporterName,
    string ContractRef,
    int ContractVersion,
    string RateCode,
    int RateVersion,
    string Lane,
    decimal? WeightKg,
    decimal? DistanceKm,
    decimal? VolumeCbm,
    string? SelectedSlab,
    decimal BaseFreight,
    decimal Dph,
    decimal Accessorials,
    decimal Discount,
    decimal FinalFreight,
    string CalculationVersion,
    DateTimeOffset RatedAt,
    string? Service = null,
    string? VehicleType = null);

/// <summary>Whether freight on a lane that has real loads is covered by a rate, and how.</summary>
/// <param name="CoverType">Lane, Zone, Fallback or None.</param>
public sealed record CoverageFact(
    string Lane,
    string Origin,
    string Destination,
    string Service,
    int Loads,
    string CoverType,
    int LaneRates,
    int ZoneRates,
    int LoadsWithoutRate);

public interface IFreightContractReportingProvider
{
    Task<IReadOnlyList<ContractFact>> ContractsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RateFact>> RatesAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DphFact>> DphAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RatingFact>> RatingsAsync(ReportingWindow window, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CoverageFact>> CoverageAsync(ReportingWindow window, CancellationToken cancellationToken = default);
}

// ---- Shared

/// <summary>An exception raised in any module (delivery, tracking, transporter alert), normalised for the exceptions reports and the executive dashboard.</summary>
/// <param name="Module">Delivery, Tracking, Transporter or Planning.</param>
/// <param name="Severity">Info, Warning, High or Critical.</param>
/// <param name="Status">Open, InProgress or Resolved.</param>
public sealed record ExceptionFact(
    string Module,
    string ExceptionRef,
    string Type,
    string Severity,
    string? ShipmentRef,
    Guid? TransporterId,
    string? TransporterName,
    string? Lane,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ResolvedAt,
    string? Owner,
    string Status);
