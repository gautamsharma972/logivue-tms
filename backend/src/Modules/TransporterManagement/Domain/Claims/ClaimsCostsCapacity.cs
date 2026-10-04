using LogiVue.Tms.TransporterManagement.Domain.Common;

namespace LogiVue.Tms.TransporterManagement.Domain.Claims;

/// <summary>
/// Local record of a damage, shortage or loss claim. Stands in for the Claims module until it exists; the KPI pipeline
/// reads it only through <c>ITransporterClaimsProvider</c>.
/// </summary>
public class TransporterClaim
{
    public long Id { get; set; }
    public long TransporterId { get; set; }

    /// <summary>Reference to the load the claim concerns. Not a foreign key: loads live outside this module.</summary>
    public string? LoadReference { get; set; }
    public ClaimType ClaimType { get; set; }
    public DateOnly ClaimDate { get; set; }
    public decimal ClaimValue { get; set; }
    public ClaimStatus Status { get; set; } = ClaimStatus.Open;
    public string? Remarks { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
}

public enum ClaimType { Damage, Shortage, LossTheft }

public enum ClaimStatus { Open, Resolved }

/// <summary>
/// Agreed and invoiced amount for one load. Cost performance is the share of loads invoiced at or below the agreed rate.
/// </summary>
public class LoadCost
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public string LoadReference { get; set; } = string.Empty;
    public DateOnly ServiceDate { get; set; }
    public decimal AgreedAmount { get; set; }
    public decimal InvoicedAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// Vehicles a transporter committed for a day and how many of them were actually available. Availability is the
/// ratio of the two, summed over the period. Vendors report it through the portal.
/// </summary>
public class CapacityDay
{
    public long Id { get; set; }
    public long TransporterId { get; set; }
    public DateOnly Date { get; set; }
    public int VehiclesCommitted { get; set; }
    public int VehiclesAvailable { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UpdatedBy { get; set; } = string.Empty;
}
