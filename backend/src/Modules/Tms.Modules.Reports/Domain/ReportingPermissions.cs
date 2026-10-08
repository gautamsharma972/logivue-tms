using Tms.SharedKernel.Security;

namespace Tms.Modules.Reports.Domain;

public static class ReportingPermissions
{
    /// <summary>Open Reports &amp; Analytics, search the catalogue, keep favourites. What a report shows still depends on its own permission.</summary>
    public const string Read = "reports.read";

    public const string Executive = "reports.executive";
    public const string Planning = "reports.planning";
    public const string Transporters = "reports.transporters";
    public const string Delivery = "reports.delivery";
    public const string Tracking = "reports.tracking";

    /// <summary>Freight, contract, rating and cost figures: finance.</summary>
    public const string Contracts = "reports.contracts";

    public const string Cross = "reports.cross";
    public const string Export = "reports.export";
    public const string Schedule = "reports.schedule";

    /// <summary>Change report and KPI definitions, report settings and who may see which customers, regions and business units.</summary>
    public const string Manage = "reports.manage";

    public const string Audit = "reports.audit";

    /// <summary>A transporter's (or driver's) own scorecard, placements, deliveries and proofs. Always limited to their company by the server.</summary>
    public const string Self = "reports.self";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Read, "Reports", "Open Reports & Analytics and use the report catalogue"),
        new(Executive, "Reports", "Executive dashboard and management analytics"),
        new(Planning, "Reports", "Planning and load optimisation reports"),
        new(Transporters, "Reports", "Transporter performance, ranking, tender and placement reports"),
        new(Delivery, "Reports", "Delivery, proof of delivery, shortage and damage reports"),
        new(Tracking, "Reports", "Control tower and shipment tracking reports"),
        new(Contracts, "Reports", "Freight spend, contract, rate, diesel adjustment and rating audit reports (commercial data)"),
        new(Cross, "Reports", "Cross-module analytics: Shipment 360, lanes, cost against service"),
        new(Export, "Reports", "Export reports to CSV, Excel and PDF"),
        new(Schedule, "Reports", "Schedule reports and subscribe to them"),
        new(Manage, "Reports", "Change report definitions, KPI definitions, report settings and data scopes"),
        new(Audit, "Reports", "See who viewed and exported which report"),
        new(Self, "Reports", "A transporter's own reports (limited to their company)", ExternalAllowed: true),
    ];
}
