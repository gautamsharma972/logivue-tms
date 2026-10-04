using Tms.SharedKernel.Security;

namespace Tms.Modules.Transporters.Domain;

public static class TransporterPermissions
{
    public const string Read = "transporters.read";
    public const string Manage = "transporters.manage";

    /// <summary>Decides transporter onboarding approvals (referenced by the approval policy for <c>transporter_onboarding</c>).</summary>
    public const string Approve = "transporters.approve";

    /// <summary>Changes bank details. Separate because it is the classic payment-fraud lever.</summary>
    public const string BankManage = "transporters.bank.manage";

    /// <summary>Vendor-portal users: maintain your own company's profile, fleet, drivers and documents.</summary>
    public const string SelfManage = "transporters.self.manage";

    /// <summary>View KPIs, scorecards, rankings, executions and lanes.</summary>
    public const string PerformanceRead = "transporters.performance.read";

    /// <summary>Recalculate KPIs, generate scorecards, record executions and delay reasons, maintain lanes, rules, claims, costs and settings.</summary>
    public const string PerformanceManage = "transporters.performance.manage";

    /// <summary>Vendor portal: see your own company's performance and record milestones on your own loads.</summary>
    public const string PerformanceSelf = "transporters.performance.self";

    /// <summary>Check which transporters can take a load and see a recommendation (planners).</summary>
    public const string Select = "transporters.select";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(Select, "Transporters", "Check which transporters can take a load and get a recommendation"),
        new(PerformanceSelf, "Transporters", "Vendor portal: view own performance and record load milestones", ExternalAllowed: true),
        new(PerformanceRead, "Transporters", "View transporter performance, scorecards, rankings and executions"),
        new(PerformanceManage, "Transporters", "Recalculate performance, record executions and manage lanes, planning rules and settings"),
        new(Read, "Transporters", "View transporters, fleet and documents"),
        new(Manage, "Transporters", "Create and edit transporters, vehicles, drivers and documents"),
        new(Approve, "Transporters", "Approve transporter onboarding"),
        new(BankManage, "Transporters", "Change transporter bank details"),
        new(SelfManage, "Transporters", "Vendor portal: maintain own company, fleet, drivers and documents", ExternalAllowed: true),
    ];
}
