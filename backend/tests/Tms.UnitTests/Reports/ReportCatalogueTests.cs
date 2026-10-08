using Tms.Modules.Reports.Domain;

namespace Tms.UnitTests.Reports;

public class ReportCatalogueTests
{
    [Fact]
    public void The_catalogue_holds_the_39_core_reports_and_dashboards_with_unique_codes()
    {
        ReportCatalogue.All.Count.ShouldBe(39);
        ReportCatalogue.All.Select(r => r.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(39);
        for (var n = 1; n <= 39; n++)
        {
            ReportCatalogue.All.ShouldContain(r => r.Code.StartsWith($"R{n:00}_", StringComparison.Ordinal), $"R{n:00} is missing");
        }
    }

    [Fact]
    public void Every_report_names_its_columns_filters_permission_and_a_real_drill_target()
    {
        var codes = ReportCatalogue.All.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();
        foreach (var r in ReportCatalogue.All)
        {
            if (r.Columns.Count == 0 || r.Filters.Count == 0)
            {
                problems.Add($"{r.Code}: no columns or filters");
            }

            if (!r.Permission.StartsWith("reports.", StringComparison.Ordinal))
            {
                problems.Add($"{r.Code}: permission {r.Permission}");
            }

            if (r.Columns.Select(c => c.Field).Distinct().Count() != r.Columns.Count)
            {
                problems.Add($"{r.Code}: repeats a column");
            }

            foreach (var d in r.Drills)
            {
                if (!codes.Contains(d.TargetReport))
                {
                    problems.Add($"{r.Code}: drills into {d.TargetReport}, which does not exist");
                }

                if (r.Columns.All(c => c.Field != d.Field))
                {
                    problems.Add($"{r.Code}: drills from a column ({d.Field}) it does not have");
                }
            }

            foreach (var m in r.Measures)
            {
                if (r.Columns.All(c => c.Field != m.Field))
                {
                    problems.Add($"{r.Code}: measure {m.Field} has no column");
                }

                if (m.Kind == MeasureKind.Ratio && (r.Measures.All(x => x.Field != m.Numerator) || r.Measures.All(x => x.Field != m.Denominator)))
                {
                    problems.Add($"{r.Code}: ratio {m.Field} needs its numerator and denominator as measures");
                }
            }

            foreach (var dup in r.Columns.Where(c => c.Visible).GroupBy(c => c.Display, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            {
                problems.Add($"{r.Code}: two visible columns are both called '{dup.Key}'");
            }

            foreach (var g in r.Groupings.Where(g => r.Columns.All(c => c.Field != g.Field)))
            {
                problems.Add($"{r.Code}: groups by {g.Field}, which is not a column");
            }

            foreach (var g in r.DefaultGroupBy.Where(g => r.Groupings.All(x => x.Field != g)))
            {
                problems.Add($"{r.Code}: default grouping {g} is not offered");
            }
        }

        problems.ShouldBeEmpty(string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void Every_kpi_has_a_definition_a_source_of_truth_and_a_drill_report_that_exists()
    {
        var codes = ReportCatalogue.All.Select(r => r.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var k in KpiCatalogue.All)
        {
            k.Numerator.ShouldNotBeNullOrWhiteSpace(k.Code);
            k.Denominator.ShouldNotBeNullOrWhiteSpace(k.Code);
            k.SourceOfTruth.ShouldNotBeNullOrWhiteSpace(k.Code);
            k.Formula.ShouldNotBeNullOrWhiteSpace(k.Code);
            if (k.DrillReport is not null)
            {
                codes.ShouldContain(k.DrillReport, $"KPI {k.Code} drills into {k.DrillReport}");
            }
        }

        KpiCatalogue.All.Select(k => k.Code).Distinct().Count().ShouldBe(KpiCatalogue.All.Count);
    }

    [Fact]
    public async Task Every_report_builds_from_the_demonstration_data_without_error()
    {
        foreach (var spec in ReportCatalogue.All)
        {
            var ctx = ReportTestKit.Context(filters: spec.Code == "R36_SHIPMENT_360" ? null : null);
            var data = await spec.Build(ctx, CancellationToken.None);
            data.ShouldNotBeNull(spec.Code);
            (data.Rows.Count + data.Cards.Count + data.Totals.Count + data.Charts.Count).ShouldBeGreaterThan(0, $"{spec.Code} produced nothing");
        }
    }

    [Fact]
    public void The_documentation_names_every_kpi_and_every_report_so_it_cannot_drift_from_the_code()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "KPI_DEFINITIONS.md")))
        {
            dir = dir.Parent;
        }

        dir.ShouldNotBeNull("the repository's docs folder");
        var kpiDoc = File.ReadAllText(Path.Combine(dir.FullName, "docs", "KPI_DEFINITIONS.md"));
        var reportDoc = File.ReadAllText(Path.Combine(dir.FullName, "docs", "REPORTS_AND_ANALYTICS.md"));
        KpiCatalogue.All.Where(k => !kpiDoc.Contains($"`{k.Code}`", StringComparison.Ordinal)).Select(k => k.Code).ShouldBeEmpty("KPIs missing from KPI_DEFINITIONS.md (regenerate it from GET /api/v1/kpis)");
        ReportCatalogue.All.Where(r => !reportDoc.Contains($"`{r.Code}`", StringComparison.Ordinal)).Select(r => r.Code).ShouldBeEmpty("reports missing from REPORTS_AND_ANALYTICS.md");
    }
}
