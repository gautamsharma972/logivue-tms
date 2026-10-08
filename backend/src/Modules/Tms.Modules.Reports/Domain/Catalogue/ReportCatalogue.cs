namespace Tms.Modules.Reports.Domain;

/// <summary>The 39 core reports and dashboards, in the order the catalogue shows them.</summary>
public static class ReportCatalogue
{
    public static IReadOnlyList<ReportSpec> All { get; } = Build();

    public static ReportSpec? Find(string code) => All.FirstOrDefault(r => r.Code.Equals(code, StringComparison.OrdinalIgnoreCase));

    private static List<ReportSpec> Build()
    {
        var all = CrossReports.All().Where(r => r.Code == "R01_EXECUTIVE_DASHBOARD")
            .Concat(PlanningReports.All())
            .Concat(TransporterReports.All())
            .Concat(PodReports.All())
            .Concat(TrackingReports.All())
            .Concat(ContractReports.All())
            .Concat(CrossReports.All().Where(r => r.Code != "R01_EXECUTIVE_DASHBOARD"))
            .OrderBy(r => r.SortOrder)
            .ToList();
        var duplicate = all.GroupBy(r => r.Code, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
        return duplicate is null ? all : throw new InvalidOperationException($"Report code {duplicate.Key} is defined twice.");
    }
}
