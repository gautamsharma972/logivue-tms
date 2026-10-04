using Tms.Modules.Transporters.Domain;
using Tms.SharedKernel.Results;
using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Application.Performance;

/// <summary>
/// Who may see or change performance data. Staff need <c>transporters.performance.read</c> / <c>.manage</c>. A vendor-portal user may read
/// (and record load events for) their own company only; another company's id is answered "not found", never "forbidden".
/// </summary>
internal sealed class PerformanceAccess(ICurrentUser user)
{
    private bool Has(string permission) => user.Permissions.Contains(permission);

    public bool IsVendor => user.TransporterId is not null;

    public Guid? OwnTransporterId => user.TransporterId;

    /// <summary>Staff who may compare transporters (rankings, benchmarks, all executions).</summary>
    public bool CanSeeAll => !IsVendor && (Has(TransporterPermissions.PerformanceRead) || Has(TransporterPermissions.PerformanceManage));

    /// <summary>Staff who may check which transporters can take a load (planners). Separate from performance because planners need it without seeing scorecards.</summary>
    public bool CanSelect => !IsVendor && (Has(TransporterPermissions.Select) || Has(TransporterPermissions.PerformanceManage));

    public bool CanManage => !IsVendor && Has(TransporterPermissions.PerformanceManage);

    public Result CheckRead(Guid transporterId) => Check(transporterId, write: false);

    /// <summary>Changing performance data. Vendors may only record events for their own loads, which is checked per call with <paramref name="vendorMayWrite"/>.</summary>
    public Result CheckWrite(Guid transporterId, bool vendorMayWrite = false) => Check(transporterId, write: true, vendorMayWrite);

    private Result Check(Guid transporterId, bool write, bool vendorMayWrite = false)
    {
        if (user.TransporterId is { } own)
        {
            if (own != transporterId)
            {
                return NotFound;
            }

            return Has(TransporterPermissions.PerformanceSelf) && (!write || vendorMayWrite) ? Result.Success() : Forbidden;
        }

        var allowed = write ? Has(TransporterPermissions.PerformanceManage) : Has(TransporterPermissions.PerformanceRead) || Has(TransporterPermissions.PerformanceManage);
        return allowed ? Result.Success() : Forbidden;
    }

    public static readonly Error NotFound = Error.NotFound("transporters.not_found", "Transporter not found.");

    public static readonly Error Forbidden = Error.Forbidden("transporters.forbidden", "You are not allowed to do that.");
}
