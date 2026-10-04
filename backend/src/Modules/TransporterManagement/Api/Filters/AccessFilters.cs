using LogiVue.Tms.Shared.Authorization;
using LogiVue.Tms.Shared.Errors;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace LogiVue.Tms.TransporterManagement.Api.Filters;

/// <summary>
/// Blocks transporter (vendor) users from internal controllers. Vendors see only their own data through the vendor
/// portal, so internal screens and endpoints are closed to them outright.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalUsersOnlyAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (user.TransporterId is not null)
        {
            throw new ForbiddenException("Transporter users cannot access internal resources.", "VENDOR_ACCESS_DENIED");
        }

        await next();
    }
}

/// <summary>Limits a controller to transporter users. Their transporter identity scopes every query and command they make.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class VendorUsersOnlyAttribute(string roles = AccessRoles.VendorReaders) : Attribute, IAsyncActionFilter
{
    private readonly string[] _allowedRoles = roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
        if (user.TransporterId is null)
        {
            throw new ForbiddenException("This area is available to transporter users only.", "INTERNAL_ACCESS_ONLY");
        }

        if (!user.Roles.Any(role => _allowedRoles.Contains(role)))
        {
            throw new ForbiddenException("Your transporter role does not allow this action.", "VENDOR_ROLE_REQUIRED");
        }

        await next();
    }
}

/// <summary>
/// Role sets for API-level authorisation. Reads are open to any signed-in internal user; writes name the roles that
/// own the work. Each set is a comma-separated list, as <c>[Authorize(Roles = ...)]</c> expects.
/// </summary>
public static class AccessRoles
{
    /// <summary>Transporter master data, fleet, lanes and capabilities.</summary>
    public const string MasterData = Roles.TransportAdmin + "," + Roles.TransportManager + "," + Roles.TransportExecutive;

    /// <summary>Compliance documents: upload, verify and reject, and compliance evaluation.</summary>
    public const string Compliance = Roles.ComplianceUser + "," + Roles.TransportAdmin + "," + Roles.TransportManager;

    /// <summary>Document uploads also come from master-data editors.</summary>
    public const string DocumentEditors = Compliance + "," + Roles.TransportExecutive;

    /// <summary>Operational execution: placements, pickups, deliveries, alerts.</summary>
    public const string Operations = Roles.OperationsUser + "," + Roles.TransportExecutive + "," + Roles.TransportAdmin + "," + Roles.TransportManager;

    /// <summary>Tender creation, distribution, award and decisions on behalf of transporters.</summary>
    public const string Tendering = Roles.TransportAdmin + "," + Roles.TransportManager + "," + Roles.TransportExecutive;

    /// <summary>Proof-of-delivery review.</summary>
    public const string PodReview = Roles.ComplianceUser + "," + Roles.TransportAdmin + "," + Roles.TransportManager;

    /// <summary>KPI recalculation, which restates performance history.</summary>
    public const string Performance = Roles.FinanceUser + "," + Roles.TransportAdmin + "," + Roles.TransportManager;

    /// <summary>Planning preferences and restrictions.</summary>
    public const string Managers = Roles.TransportAdmin + "," + Roles.TransportManager;

    /// <summary>Vendor writes: accept, reject, vehicle, placement confirmation and POD submission.</summary>
    public const string VendorWriters = Roles.TransporterAdmin + "," + Roles.TransporterOperationsUser;

    /// <summary>Any transporter user, including viewers.</summary>
    public const string VendorReaders = VendorWriters + "," + Roles.TransporterViewer;
}
