using FluentValidation;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Contracts;
using Tms.SharedKernel.Paging;

namespace Tms.Modules.Shipments.Application;

public sealed record PartyDto(string Name, string Line1, string City, string State, string Pincode, string? ContactName, string? ContactPhone)
{
    public Party ToParty() => new(Name, Line1, City, State, Pincode, ContactName, ContactPhone);

    public static PartyDto From(Party p) => new(p.Name, p.Line1, p.City, p.State, p.Pincode, p.ContactName, p.ContactPhone);
}

public sealed record SaveOrderRequest(
    OrderDirection Direction,
    string? Reference,
    PartyDto Pickup,
    PartyDto Drop,
    decimal WeightKg,
    decimal? VolumeCbm,
    int? Packages,
    string Description,
    DateOnly ReadyDate,
    DateOnly? DeliverByDate,
    string? Notes,
    Guid? PickupLocationId = null,
    Guid? DropLocationId = null,
    TimeOnly? DeliveryWindowFrom = null,
    TimeOnly? DeliveryWindowTo = null,
    OrderPriority Priority = OrderPriority.Normal,
    string? ProductCategory = null,
    HandlingType Handling = HandlingType.Standard,
    bool IsHazardous = false,
    bool IsStackable = true,
    decimal? LongestItemM = null,
    ReturnType? ReturnType = null,
    string? ReturnReason = null,
    TimeOnly? PickupWindowFrom = null,
    TimeOnly? PickupWindowTo = null);

public sealed record ReasonRequest(string Reason);

public sealed record OrderDto(
    Guid Id,
    string Number,
    OrderDirection Direction,
    OrderStatus Status,
    string? Reference,
    PartyDto Pickup,
    PartyDto Drop,
    decimal WeightKg,
    decimal? VolumeCbm,
    int? Packages,
    string Description,
    DateOnly ReadyDate,
    DateOnly? DeliverByDate,
    string? Notes,
    Guid? ShipmentId,
    string? ShipmentNumber,
    string? CancelReason,
    long Version,
    Guid? PickupLocationId = null,
    Guid? DropLocationId = null,
    TimeOnly? DeliveryWindowFrom = null,
    TimeOnly? DeliveryWindowTo = null,
    OrderPriority Priority = OrderPriority.Normal,
    string? ProductCategory = null,
    HandlingType Handling = HandlingType.Standard,
    bool IsHazardous = false,
    bool IsStackable = true,
    decimal? LongestItemM = null,
    ReturnType? ReturnType = null,
    string? ReturnReason = null,
    TimeOnly? PickupWindowFrom = null,
    TimeOnly? PickupWindowTo = null);

public sealed record CompatibilityRuleDto(Guid Id, string CategoryA, string CategoryB, string? Reason);

public sealed record SaveCompatibilityRuleRequest(string CategoryA, string CategoryB, string? Reason);

public sealed record ListOrdersQuery(
    OrderStatus? Status = null,
    OrderDirection? Direction = null,
    string? Search = null,
    string? PickupState = null,
    int Page = 1,
    int PageSize = 25);

// ---- shipments

public sealed record CreateShipmentRequest(
    IReadOnlyList<Guid> OrderIds, FreightMode Mode, Guid? VehicleTypeId, DateOnly PlannedPickupDate, decimal? DistanceKm);

public sealed record UpdateShipmentPlanRequest(FreightMode Mode, Guid? VehicleTypeId, DateOnly PlannedPickupDate, decimal? DistanceKm);

public sealed record AddOrdersRequest(IReadOnlyList<Guid> OrderIds);

public sealed record SequenceRequest(IReadOnlyList<Guid> OrderIds);

/// <param name="OverrideReason">Required when the chosen contract is not the cheapest.</param>
public sealed record TenderRequest(Guid ContractId, string? OverrideReason);

public sealed record AcceptRequest(Guid VehicleId, Guid DriverId);

public sealed record ListShipmentsQuery(
    ShipmentStatus? Status = null,
    Guid? TransporterId = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

public sealed record QuoteLineDto(string Code, string Description, decimal Amount);

public sealed record ShipmentOrderDto(
    Guid OrderId,
    string OrderNumber,
    int DropSequence,
    string? LrNumber,
    PartyDto Pickup,
    PartyDto Drop,
    decimal WeightKg,
    decimal? VolumeCbm,
    string Description,
    bool IsReturn = false,
    DateTimeOffset? DeliveredAt = null,
    string? ReceiverName = null,
    int? PackagesShipped = null,
    int? DeliveredPackages = null,
    int? DamagedPackages = null,
    int? ShortagePackages = null,
    string? DeliveryRemarks = null,
    PodStatus PodStatus = PodStatus.Awaiting,
    string? PodRejectionReason = null,
    int PodDocuments = 0);

public sealed record ShipmentSummaryDto(
    Guid Id,
    string Number,
    ShipmentStatus Status,
    FreightMode Mode,
    string Lane,
    DateOnly PlannedPickupDate,
    int OrderCount,
    decimal TotalWeightKg,
    Guid? TransporterId,
    string? TransporterName,
    string? VehicleRegistration,
    decimal? Utilization,
    decimal? FreightEstimate);

/// <summary>Full shipment. <see cref="FreightEstimate"/>, <see cref="EstimateLines"/> and <see cref="OverrideReason"/> are null for vendors.</summary>
public sealed record ShipmentDto(
    ShipmentSummaryDto Summary,
    Guid? VehicleTypeId,
    string? VehicleTypeName,
    decimal? DistanceKm,
    decimal? TotalVolumeCbm,
    Guid? ContractId,
    string? ContractReference,
    IReadOnlyList<QuoteLineDto>? EstimateLines,
    string? OverrideReason,
    DateTimeOffset? TenderedAt,
    int RejectionCount,
    string? LastRejectionReason,
    Guid? VehicleId,
    Guid? DriverId,
    string? DriverName,
    string? DriverPhone,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? DispatchedAt,
    DateTimeOffset? DeliveredAt,
    string? CancelReason,
    IReadOnlyList<ShipmentOrderDto> Orders,
    long Version,
    string? PlanReference = null,
    decimal? PlannedCost = null);

public sealed record ShipmentQuoteDto(
    Guid ContractId,
    string ContractReference,
    Guid TransporterId,
    string TransporterName,
    FreightMode Mode,
    string Lane,
    decimal Total,
    bool IsCheapest,
    IReadOnlyList<QuoteLineDto> Lines,
    IReadOnlyList<string> Notes);

public sealed record ShipmentQuotesDto(IReadOnlyList<ShipmentQuoteDto> Quotes, string? Message);

public sealed record FleetOptionDto(Guid Id, string Label, bool IsOk, bool Warn, IReadOnlyList<string> Issues);

public sealed record FleetOptionsDto(IReadOnlyList<FleetOptionDto> Vehicles, IReadOnlyList<FleetOptionDto> Drivers);

// ---- planning

public sealed record AdviceRequest(
    decimal WeightKg, decimal? VolumeCbm, string OriginState, string? OriginCity, string DestinationState, string? DestinationCity,
    DateOnly? Date, decimal? DistanceKm);

public sealed record VehicleOptionDto(Guid VehicleTypeId, string Name, int PayloadKg, decimal WeightUtilization);

public sealed record ModeOptionDto(FreightMode Mode, bool Feasible, decimal? Total, string? Transporter, string Detail);

public sealed record AdviceDto(
    IReadOnlyList<VehicleOptionDto> Fitting,
    VehicleOptionDto? Recommended,
    int? VehiclesNeeded,
    string? SizingWarning,
    IReadOnlyList<ModeOptionDto> Modes,
    FreightMode? RecommendedMode,
    string ModeReason);

public sealed record SuggestedLoadDto(
    IReadOnlyList<Guid> OrderIds,
    IReadOnlyList<string> OrderNumbers,
    string PickupCity,
    string PickupState,
    IReadOnlyList<string> Drops,
    decimal TotalWeightKg,
    decimal? TotalVolumeCbm,
    VehicleOptionDto? Vehicle,
    decimal Utilization,
    FreightMode Suggested,
    DateOnly EarliestReady,
    DateOnly? EarliestDeadline,
    IReadOnlyList<Guid> BackhaulOrderIds,
    string? Warning);

public sealed record UtilizationQuery(DateOnly? From = null, DateOnly? To = null, Guid? TransporterId = null);

public sealed record UtilizationRowDto(
    Guid ShipmentId, string Number, DateOnly PlannedPickupDate, string TransporterName, string? VehicleRegistration, decimal LoadKg,
    int? PayloadKg, decimal? Utilization);

public sealed record UtilizationDto(int Shipments, decimal? AverageUtilization, int UnderUtilised, IReadOnlyList<UtilizationRowDto> Rows);

internal sealed class SaveOrderRequestValidator : AbstractValidator<SaveOrderRequest>
{
    public SaveOrderRequestValidator()
    {
        // Field rules (pincode, weight, dates…) live in the domain; this guards the request's shape.
        RuleFor(x => x.Pickup).NotNull();
        RuleFor(x => x.Drop).NotNull();
        RuleFor(x => x.Description).NotNull();
        RuleFor(x => x.Direction).IsInEnum();
    }
}

internal sealed class SaveCompatibilityRuleRequestValidator : AbstractValidator<SaveCompatibilityRuleRequest>
{
    public SaveCompatibilityRuleRequestValidator()
    {
        RuleFor(x => x.CategoryA).NotNull();
        RuleFor(x => x.CategoryB).NotNull();
    }
}

internal sealed class ReasonRequestValidator : AbstractValidator<ReasonRequest>
{
    public ReasonRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
}

internal sealed class CreateShipmentRequestValidator : AbstractValidator<CreateShipmentRequest>
{
    public CreateShipmentRequestValidator()
    {
        RuleFor(x => x.OrderIds).NotNull().Must(ids => ids is { Count: > 0 and <= 200 }).WithMessage("Choose between 1 and 200 orders.");
        RuleFor(x => x.Mode).IsInEnum();
    }
}

internal sealed class UpdateShipmentPlanRequestValidator : AbstractValidator<UpdateShipmentPlanRequest>
{
    public UpdateShipmentPlanRequestValidator() => RuleFor(x => x.Mode).IsInEnum();
}

internal sealed class AddOrdersRequestValidator : AbstractValidator<AddOrdersRequest>
{
    public AddOrdersRequestValidator() =>
        RuleFor(x => x.OrderIds).NotNull().Must(ids => ids is { Count: > 0 and <= 200 }).WithMessage("Choose between 1 and 200 orders.");
}

internal sealed class SequenceRequestValidator : AbstractValidator<SequenceRequest>
{
    public SequenceRequestValidator() => RuleFor(x => x.OrderIds).NotNull();
}

internal sealed class TenderRequestValidator : AbstractValidator<TenderRequest>
{
    public TenderRequestValidator()
    {
        RuleFor(x => x.ContractId).NotEmpty();
        RuleFor(x => x.OverrideReason).MaximumLength(500);
    }
}

internal sealed class AcceptRequestValidator : AbstractValidator<AcceptRequest>
{
    public AcceptRequestValidator()
    {
        RuleFor(x => x.VehicleId).NotEmpty();
        RuleFor(x => x.DriverId).NotEmpty();
    }
}

internal sealed class AdviceRequestValidator : AbstractValidator<AdviceRequest>
{
    public AdviceRequestValidator()
    {
        RuleFor(x => x.WeightKg).GreaterThan(0);
        RuleFor(x => x.OriginState).NotEmpty();
        RuleFor(x => x.DestinationState).NotEmpty();
    }
}
