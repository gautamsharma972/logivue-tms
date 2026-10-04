using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application;

internal enum AccessLevel
{
    Read,
    Manage,
}

/// <summary>
/// Who may touch which transporter's data. Internal staff are governed by <c>transporters.read/manage</c>;
/// vendor-portal users (those linked to a transporter) may only reach their own company, and only with
/// <c>transporters.self.manage</c>. Another company's id is reported as "not found", never "forbidden",
/// so ids cannot be probed.
/// </summary>
internal sealed class TransporterAccess(ICurrentUser user)
{
    public Guid? OwnTransporterId => user.TransporterId;

    public bool IsVendor => user.TransporterId is not null;

    private bool Has(string permission) => user.Permissions.Contains(permission);

    /// <summary>May list/browse the transporter master (internal staff).</summary>
    public bool CanBrowseAll => !IsVendor && (Has(TransporterPermissions.Read) || Has(TransporterPermissions.Manage));

    /// <summary>May use the module at all (needed for shared reference data such as vehicle types).</summary>
    public bool CanUseModule => CanBrowseAll || (IsVendor && Has(TransporterPermissions.SelfManage));

    public bool CanManageBank => !IsVendor && Has(TransporterPermissions.BankManage);

    public bool CanManageInternally => !IsVendor && Has(TransporterPermissions.Manage);

    public Result Check(Guid transporterId, AccessLevel level)
    {
        if (user.TransporterId is { } own)
        {
            if (own != transporterId)
            {
                return NotFound;
            }

            return Has(TransporterPermissions.SelfManage)
                ? Result.Success()
                : Error.Forbidden("transporters.forbidden", "You are not allowed to do that.");
        }

        var allowed = level == AccessLevel.Manage
            ? Has(TransporterPermissions.Manage)
            : Has(TransporterPermissions.Read) || Has(TransporterPermissions.Manage);

        return allowed ? Result.Success() : Error.Forbidden("transporters.forbidden", "You are not allowed to do that.");
    }

    public static readonly Error NotFound = Error.NotFound("transporters.not_found", "Transporter not found.");

    public static readonly Error Forbidden = Error.Forbidden("transporters.forbidden", "You are not allowed to do that.");
}
