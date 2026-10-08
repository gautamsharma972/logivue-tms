using Tms.Modules.Reports.Domain;
using Tms.Modules.Reports.Infrastructure.Providers;

namespace Tms.UnitTests.Reports;

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>A report run over the demonstration dataset, as of a fixed day, so numbers can be asserted.</summary>
internal static class ReportTestKit
{
    public static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateOnly Today = new(2026, 10, 7);

    public static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static readonly ReportSettings Settings = new();

    public static DemoReportingData Demo { get; } = new(new FixedClock(Now));

    public static ReportingProviders Providers => new(Demo, Demo, Demo, Demo, Demo);

    public static ReportPrincipal Admin(params string[] extra) => new(Guid.NewGuid(), TenantId, new HashSet<string>(
        [ReportingPermissions.Read, ReportingPermissions.Executive, ReportingPermissions.Planning, ReportingPermissions.Transporters, ReportingPermissions.Delivery, ReportingPermissions.Tracking,
            ReportingPermissions.Contracts, ReportingPermissions.Cross, ReportingPermissions.Export, ReportingPermissions.Schedule, .. extra]), null, new Dictionary<string, IReadOnlyList<string>>());

    public static ReportPrincipal Vendor(Guid transporterId) => new(Guid.NewGuid(), TenantId, new HashSet<string> { ReportingPermissions.Self }, transporterId, new Dictionary<string, IReadOnlyList<string>>());

    public static ReportFacts Facts(DateRange? range = null, ReportFilters? filters = null, ReportPrincipal? principal = null, ReportSettings? settings = null)
    {
        var r = range ?? new DateRange(Today.AddDays(-29), Today);
        return new ReportFacts(Providers, r, filters ?? new ReportFilters(), principal ?? Admin(), settings ?? Settings, Now, new DateRange(Today.AddDays(-400), Today.AddDays(5)));
    }

    public static ReportContext Context(DateRange? range = null, ReportFilters? filters = null, ReportPrincipal? principal = null, string grain = "month")
    {
        var facts = Facts(range, filters, principal);
        return new ReportContext(facts, PeriodKind.Custom, CompareMode.PreviousPeriod, grain);
    }
}
