using Microsoft.EntityFrameworkCore;
using Tms.Modules.Shipments.Domain;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Shipments.Application;

/// <summary>
/// Staff plan and read; vendor-portal users (those tied to a transporter) may only respond to loads tendered to their own
/// company, and never see what the load is estimated to cost.
/// </summary>
internal sealed class ShipmentAccess(ICurrentUser user)
{
    public bool IsVendor => user.TransporterId is not null;

    public Guid? VendorTransporterId => user.TransporterId;

    private bool Has(string permission) => user.Permissions.Contains(permission);

    public bool CanRead => !IsVendor && (Has(ShipmentPermissions.Read) || Has(ShipmentPermissions.Plan));

    public bool CanPlan => !IsVendor && Has(ShipmentPermissions.Plan);

    public bool CanVerifyPod => !IsVendor && Has(ShipmentPermissions.PodVerify);

    public bool CanApprove => !IsVendor && Has(ShipmentPermissions.Approve);

    /// <summary>Vendors with the respond permission, or planners acting on a transporter's behalf (a phone call, an email).</summary>
    public bool CanRespond => IsVendor ? Has(ShipmentPermissions.Respond) : CanPlan;

    /// <summary>Whether this caller may see the shipment at all (vendors: only their own, once it has been offered to them).</summary>
    public bool CanSee(Shipment shipment) =>
        IsVendor
            ? Has(ShipmentPermissions.Respond) && shipment.TransporterId == VendorTransporterId && shipment.Status != ShipmentStatus.Draft
            : CanRead;

    public static readonly Error Forbidden = Error.Forbidden("shipments.forbidden", "You are not allowed to do that.");

    public static readonly Error ShipmentNotFound = Error.NotFound("shipments.not_found", "Shipment not found.");

    public static readonly Error OrderNotFound = Error.NotFound("orders.not_found", "Order not found.");
}

internal static class Support
{
    public static DateOnly TodayInIndia(this TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(TimeSpan.FromMinutes(330)).DateTime);

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.DuplicateKeyEntry };

    public static string LaneLabel(string originCity, string originState, string destCity, string destState) =>
        $"{originCity}, {originState} → {destCity}, {destState}";
}
