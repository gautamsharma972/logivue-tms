using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Domain.Placement;

public class VehiclePlacementRequest
{
    public long Id { get; set; }
    public string LoadReference { get; set; } = string.Empty;
    public long TransporterId { get; set; }
    public long? VehicleTypeReference { get; set; }
    public DateTime RequestedAt { get; set; }
    public DateTime RequiredPlacementAt { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ReportedAt { get; set; }
    public DateTime? PlacedAt { get; set; }
    public DateTime? LoadingStartedAt { get; set; }
    public PlacementStatus Status { get; set; } = PlacementStatus.Requested;
    /// <summary>Reason recorded for a no-show or cancellation.</summary>
    public string? ExceptionReason { get; set; }
    public long? VehicleId { get; set; }
    public int ReplacementCount { get; set; }
    public DateTime? CancelledAt { get; set; }
}

public class VehiclePlacementEvent
{
    public long Id { get; set; }
    public long PlacementRequestId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime EventAt { get; set; }
    public long? VehicleId { get; set; }
    public string? Remarks { get; set; }
}
