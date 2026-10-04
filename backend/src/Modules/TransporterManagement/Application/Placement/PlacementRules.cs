using LogiVue.Tms.TransporterManagement.Domain.Common;
using LogiVue.Tms.TransporterManagement.Domain.Placement;

namespace LogiVue.Tms.TransporterManagement.Application.Placement;

/// <summary>Placement state rules and SLA classification. Pure, so the SLA can be reasoned about and tested in isolation.</summary>
public static class PlacementRules
{
    /// <summary>States in which the transporter still owes the placement.</summary>
    public static bool IsOpen(PlacementStatus status) =>
        status is PlacementStatus.Requested or PlacementStatus.Confirmed or PlacementStatus.VehicleAssigned or PlacementStatus.Reported;

    public static bool IsPlaced(PlacementStatus status) =>
        status is PlacementStatus.Placed or PlacementStatus.LoadingStarted;

    public static string SlaStatus(VehiclePlacementRequest placement, DateTime now, int graceMinutes)
    {
        var deadline = placement.RequiredPlacementAt.AddMinutes(graceMinutes);
        return placement.Status switch
        {
            PlacementStatus.NoShow => "NoShow",
            PlacementStatus.Cancelled => "Cancelled",
            _ when IsPlaced(placement.Status) => placement.PlacedAt <= deadline ? "OnTime" : "Late",
            _ => now > deadline ? "Overdue" : "Pending"
        };
    }

    /// <summary>Signed minutes between the required and actual placement. Positive means late.</summary>
    public static int? DelayMinutes(VehiclePlacementRequest placement) =>
        placement.PlacedAt is { } placed ? (int)Math.Round((placed - placement.RequiredPlacementAt).TotalMinutes) : null;
}
