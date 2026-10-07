using Tms.SharedKernel.Domain;

namespace Tms.SharedKernel.Contracts;

public enum FreightServiceType
{
    Ftl = 1,
    Ptl = 2,
    Dedicated = 3,
}

/// <summary>
/// What any module asks the contract rating engine. Places are named the way shipments name them (state and city), vehicle types by id. <see cref="Commit"/> keeps the
/// rating as the shipment's contractual freight (with the shipment reference); without it nothing is stored, so estimates and simulations leave no trace.
/// </summary>
public sealed record FreightRatingRequest(
    DateOnly ShipmentDate,
    string OriginState,
    string? OriginCity,
    string DestinationState,
    string? DestinationCity,
    FreightServiceType Service,
    Guid? TransporterId = null,
    Guid? VehicleTypeId = null,
    decimal WeightKg = 0,
    decimal VolumeCbm = 0,
    decimal DistanceKm = 0,
    int StopCount = 1,
    IReadOnlyList<string>? RequiredCapabilities = null,
    IReadOnlyDictionary<string, decimal>? AccessorialInputs = null,
    string? ShipmentReference = null,
    bool Commit = false);

public sealed record RatingComponentFact(string Type, string Description, decimal? Quantity, string? Unit, decimal? Rate, decimal Amount, string? Reference, int Sequence);

/// <summary>Why a contract or rate was not used, in a code a program can test and words a person can read.</summary>
public sealed record RateExclusionFact(string ContractReference, string? RateReference, string ReasonCode, string Reason);

public sealed record FreightRatingResult(
    bool Qualified,
    string? ErrorCode,
    string? Message,
    Guid? RatingId,
    string? RatingReference,
    Guid? ContractId,
    string? ContractReference,
    int? ContractVersion,
    Guid? TransporterId,
    string? RateReference,
    int? RateVersion,
    string? DphRuleReference,
    int? DphVersion,
    decimal BaseFreight,
    decimal DphAdjustment,
    decimal AccessorialAmount,
    decimal DiscountAmount,
    decimal TotalFreight,
    string Currency,
    string CalculationVersion,
    IReadOnlyList<RatingComponentFact> Components,
    IReadOnlyList<string> Reasons,
    IReadOnlyList<RateExclusionFact> Exclusions);

/// <summary>Every rate that qualifies for a shipment, best first, so planning can weigh freight options.</summary>
public sealed record FreightRatingOptions(IReadOnlyList<FreightRatingResult> Options, IReadOnlyList<RateExclusionFact> Exclusions, string? Message);

/// <summary>Planning asks for the contractual freight of a planned load. The caller authorises its own users.</summary>
public interface IFreightPlanningIntegration
{
    Task<FreightRatingResult> CalculatePlanningFreightAsync(FreightRatingRequest request, CancellationToken cancellationToken = default);

    Task<FreightRatingOptions> GetFreightOptionsAsync(FreightRatingRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Transporter management asks whether a carrier has contract coverage; eligibility rules stay in Transporters.</summary>
public interface IFreightTransporterIntegration
{
    Task<bool> HasActiveCommercialCoverageAsync(
        Guid transporterId, string originState, string? originCity, string destinationState, string? destinationCity, FreightServiceType service, DateOnly? date = null,
        CancellationToken cancellationToken = default);
}

public sealed record FreightAuditRequest(string? ShipmentReference, FreightRatingRequest? Rating);

/// <summary>What the contract says a shipment should have cost, for freight audit to compare an invoice against.</summary>
public sealed record ContractualFreightBaseline(
    bool Found,
    string? Message,
    string? ContractReference,
    int? ContractVersion,
    string? RateReference,
    int? RateVersion,
    decimal ExpectedBaseFreight,
    decimal ExpectedDph,
    decimal ExpectedAccessorials,
    decimal ExpectedDiscount,
    decimal ExpectedTotal,
    string Currency,
    string CalculationVersion,
    bool FromCommittedRating,
    IReadOnlyList<RatingComponentFact> Components);

/// <summary>
/// Freight audit reads the contractual baseline here (the spec's <c>IFreightAuditIntegration.GetContractualBaselineAsync</c>; renamed because <c>IFreightAuditIntegration</c> already
/// names the call the other way, from Deliveries to audit).
/// </summary>
public interface IContractualBaselineService
{
    Task<ContractualFreightBaseline> GetContractualBaselineAsync(FreightAuditRequest request, CancellationToken cancellationToken = default);
}

public sealed record ContractedCapacityFact(
    Guid ContractId, string ContractReference, Guid TransporterId, Guid? VehicleTypeId, string? VehicleTypeName, int CommittedVehicleCount, decimal? CommittedCapacityKg, int? MinimumMonthlyTrips,
    decimal? MinimumMonthlyTonnage, decimal? TargetBusinessSharePct, DateOnly? From, DateOnly? To);

public sealed record ContractSlaFact(
    Guid ContractId, string ContractReference, Guid TransporterId, FreightServiceType Service, string? OriginState, string? OriginCity, string? DestinationState, string? DestinationCity,
    int? PickupSlaMinutes, int? TransitSlaMinutes, int? DeliverySlaMinutes, int? TenderLeadTimeMinutes, IReadOnlyList<string> OperatingDays, TimeOnly? CutoffTime);

/// <summary>What the contracts promise and require: committed vehicles, minimum trips, service levels. Planning and transporter management read it; they do not own it.</summary>
public interface IContractedCapacityProvider
{
    Task<IReadOnlyList<ContractedCapacityFact>> GetCapacityAsync(Guid? transporterId, DateOnly date, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContractSlaFact>> GetSlaAsync(Guid? transporterId, DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>What actually happened on a shipment that can create or change a charge: more stops, waiting, kilometres, detention. Delivery and tracking will supply it.</summary>
public sealed record FreightActuals(
    decimal? ActualDistanceKm, int? ActualStopCount, decimal? DetentionHours, decimal? WaitingHours, decimal? ExtraKm, IReadOnlyDictionary<string, decimal>? Other);

/// <summary>
/// The rating engine consumes this to price what really happened. Nothing implements it yet: a local stand-in answers "no actuals", so rating works from the request alone.
/// POD / Delivery and Tracking can implement it later without Contracts knowing about them.
/// </summary>
public interface IFreightActualsProvider
{
    Task<FreightActuals?> GetActualsAsync(string shipmentReference, CancellationToken cancellationToken = default);
}

// ---- events (transactional outbox, so subscribers must be idempotent)

public abstract record FreightContractEvent(Guid TenantId, Guid ContractId, string ContractReference) : DomainEvent;

public sealed record ContractActivated(Guid TenantId, Guid ContractId, string ContractReference, Guid TransporterId, string Services, DateOnly From, DateOnly To, int RateCount)
    : FreightContractEvent(TenantId, ContractId, ContractReference);

public sealed record ContractSuspended(Guid TenantId, Guid ContractId, string ContractReference, string Reason) : FreightContractEvent(TenantId, ContractId, ContractReference);

public sealed record ContractExpired(Guid TenantId, Guid ContractId, string ContractReference, DateOnly EndedOn) : FreightContractEvent(TenantId, ContractId, ContractReference);

public sealed record ContractRenewalDue(Guid TenantId, Guid ContractId, string ContractReference, DateOnly EndsOn, int DaysLeft) : FreightContractEvent(TenantId, ContractId, ContractReference);

public sealed record RateActivated(Guid TenantId, Guid ContractId, string ContractReference, int RateCount) : FreightContractEvent(TenantId, ContractId, ContractReference);

public sealed record RateSuperseded(Guid TenantId, Guid ContractId, string ContractReference, DateOnly LastDay) : FreightContractEvent(TenantId, ContractId, ContractReference);

public sealed record RateExpired(Guid TenantId, Guid ContractId, string ContractReference, int RateCount) : FreightContractEvent(TenantId, ContractId, ContractReference);

public sealed record DphRevisionApplied(Guid TenantId, Guid ContractId, string ContractReference, string RuleCode, DateOnly PeriodStart, decimal ReferencePrice, decimal AdjustmentPercent)
    : FreightContractEvent(TenantId, ContractId, ContractReference);

public sealed record RatingCalculated(Guid TenantId, Guid RatingId, string RatingReference, string? ShipmentReference, string ContractReference, decimal TotalFreight, string Currency, string CalculationVersion)
    : DomainEvent;

public sealed record RatingFailed(Guid TenantId, Guid RatingId, string RatingReference, string? ShipmentReference, string ErrorCode, string Message) : DomainEvent;

public sealed record RateConflictDetected(Guid TenantId, Guid RatingId, string RatingReference, string? ShipmentReference, string Message) : DomainEvent;
