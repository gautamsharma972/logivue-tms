using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Domain.Tendering;

/// <summary>
/// One row per transporter invitation. A broadcast tender is several rows sharing a <see cref="TenderNumber"/>.
/// </summary>
public class Tender
{
    public long Id { get; set; }
    public string TenderNumber { get; set; } = string.Empty;
    public string LoadReference { get; set; } = string.Empty;
    public long TransporterId { get; set; }
    public TenderType TenderType { get; set; }
    public decimal? OfferedRate { get; set; }
    public long? VehicleTypeReference { get; set; }
    public DateTime PickupDateTime { get; set; }
    public DateTime DeliveryDateTime { get; set; }
    public DateTime ResponseDeadline { get; set; }

    /// <summary>Position in a sequential tender; null for direct and broadcast tenders.</summary>
    public int? SequenceNumber { get; set; }

    public long OriginLocationReference { get; set; }
    public long DestinationLocationReference { get; set; }
    public string ServiceType { get; set; } = string.Empty;
    public decimal WeightKg { get; set; }
    public decimal? VolumeM3 { get; set; }
    public string Currency { get; set; } = "INR";
    public string? Notes { get; set; }
    public DateTime? SentAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;

    public TenderStatus Status { get; set; } = TenderStatus.Draft;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class TenderResponse
{
    public long Id { get; set; }
    public long TenderId { get; set; }
    public long TransporterId { get; set; }
    public TenderResponseType Response { get; set; }
    public DateTime ResponseAt { get; set; }
    public decimal? QuotedRate { get; set; }
    public long? VehicleId { get; set; }
    public string? DriverReference { get; set; }
    public string? DriverMobile { get; set; }
    public DateTime? ExpectedPlacementAt { get; set; }
    public DateTime? EtaAt { get; set; }
    public string? Reason { get; set; }
    public string? Comments { get; set; }
}

public class TenderEvent
{
    public long Id { get; set; }
    public long TenderId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime EventAt { get; set; }
    public string PerformedBy { get; set; } = string.Empty;
    public string? Comments { get; set; }
}
