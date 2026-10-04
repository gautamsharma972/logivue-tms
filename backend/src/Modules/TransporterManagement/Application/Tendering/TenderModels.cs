using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Tendering;

/// <summary>One transporter's invitation within a tender. Broadcast and sequential tenders are several invitations sharing a number.</summary>
public sealed record TenderInvitationDto(
    long Id,
    string TenderNumber,
    TenderType TenderType,
    long TransporterId,
    string TransporterName,
    TenderStatus Status,
    int? SequenceNumber,
    string LoadReference,
    long OriginLocationReference,
    long DestinationLocationReference,
    string ServiceType,
    long? VehicleTypeReference,
    decimal WeightKg,
    decimal? VolumeM3,
    decimal? OfferedRate,
    string Currency,
    DateTime PickupDateTime,
    DateTime DeliveryDateTime,
    DateTime ResponseDeadline,
    DateTime? SentAt,
    string? Notes,
    DateTime CreatedAt);

public sealed record TenderEventDto(long Id, string EventType, DateTime EventAt, string PerformedBy, string? Comments);

public sealed record TenderResponseDto(
    long Id,
    TenderResponseType Response,
    DateTime ResponseAt,
    decimal? QuotedRate,
    long? VehicleId,
    string? DriverReference,
    string? DriverMobile,
    DateTime? ExpectedPlacementAt,
    DateTime? EtaAt,
    string? Reason,
    string? Comments);

public sealed record TenderDetailDto(
    TenderInvitationDto Invitation,
    IReadOnlyList<TenderInvitationDto> Group,
    IReadOnlyList<TenderResponseDto> Responses,
    IReadOnlyList<TenderEventDto> Events);

public sealed record CreateTenderRequest(
    TenderType TenderType,
    string LoadReference,
    long OriginLocationReference,
    long DestinationLocationReference,
    string ServiceType,
    long? VehicleTypeReference,
    decimal WeightKg,
    decimal? VolumeM3,
    DateTime PickupDateTime,
    DateTime DeliveryDateTime,
    DateTime? ResponseDeadline,
    decimal? OfferedRate,
    string? Currency,
    IReadOnlyList<long> TransporterIds,
    string? Notes);

public sealed record AcceptTenderRequest(string? Comments, decimal? QuotedRate = null);

public sealed record RejectTenderRequest(string ReasonCode, string? Comments);

public sealed record CounterOfferRequest(decimal Rate, string? Comments);

public sealed record VehicleAssignmentRequest(
    string RegistrationNumber,
    string DriverName,
    string DriverMobile,
    DateTime? ExpectedPlacementAt,
    DateTime? EtaAt);

public sealed record TenderSearch(string? TenderNumber, TenderStatus? Status, long? TransporterId, int Page = 1, int PageSize = 25);

public sealed record VendorDashboardDto(
    int NewTenders,
    int PendingAcceptance,
    int AcceptedLoads,
    int UpcomingPlacements,
    int TodaysPickups,
    int TodaysDeliveries,
    int? PendingPod,
    int OpenExceptions);

/// <summary>A load the vendor has accepted, with its vehicle and placement progress. Execution states after placement arrive in a later milestone.</summary>
public sealed record VendorLoadDto(
    long InvitationId,
    string TenderNumber,
    string LoadReference,
    TenderStatus Status,
    string LoadStatus,
    string ServiceType,
    long OriginLocationReference,
    long DestinationLocationReference,
    decimal WeightKg,
    DateTime PickupDateTime,
    DateTime DeliveryDateTime,
    string? VehicleRegistration,
    string? DriverName,
    string? PlacementStatus,
    string? ExecutionStatus = null,
    string? PodStatus = null);
