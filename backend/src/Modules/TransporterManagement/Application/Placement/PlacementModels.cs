using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Application.Placement;

public sealed record CreatePlacementRequest(
    string LoadReference,
    long TransporterId,
    DateTime RequiredPlacementAt,
    long? VehicleTypeReference = null);

public sealed record PlacementEventDto(string EventType, DateTime EventAt, long? VehicleId, string? Remarks);

/// <param name="SlaStatus">OnTime, Late, Pending, Overdue, NoShow or Cancelled. Computed against the placement grace period.</param>
/// <param name="PlacementDelayMinutes">Minutes after the required placement time. Null until the vehicle is placed.</param>
public sealed record PlacementDto(
    long Id,
    string LoadReference,
    long TransporterId,
    long? VehicleTypeReference,
    long? VehicleId,
    string? VehicleRegistration,
    DateTime RequestedAt,
    DateTime RequiredPlacementAt,
    DateTime? ConfirmedAt,
    DateTime? ReportedAt,
    DateTime? PlacedAt,
    DateTime? LoadingStartedAt,
    PlacementStatus Status,
    string SlaStatus,
    int? PlacementDelayMinutes,
    int ReplacementCount,
    string? Reason,
    IReadOnlyList<PlacementEventDto> Events);
